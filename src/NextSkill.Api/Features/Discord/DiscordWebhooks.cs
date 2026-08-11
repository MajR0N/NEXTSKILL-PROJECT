using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace NextSkill.Api.Features.Discord;

/// <summary>Validation at the credential boundary (decisions 014, 040).</summary>
public static class DiscordWebhookUrls
{
    public static bool TryParse(string? value, out Uri? uri, out string error)
    {
        uri = null;
        error = "Podaj prawidłowy adres webhooka Discord.";

        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(parsed.IdnHost, "discord.com", StringComparison.OrdinalIgnoreCase) ||
            (!parsed.IsDefaultPort && parsed.Port != 443) ||
            parsed.UserInfo.Length > 0 || parsed.Query.Length > 0 || parsed.Fragment.Length > 0 ||
            parsed.AbsolutePath.EndsWith('/'))
            return false;

        var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 4 ||
            !string.Equals(segments[0], "api", StringComparison.Ordinal) ||
            !string.Equals(segments[1], "webhooks", StringComparison.Ordinal) ||
            segments[2].Length is < 15 or > 22 || !segments[2].All(char.IsAsciiDigit) ||
            segments[3].Length is < 20 or > 200 ||
            !segments[3].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'))
            return false;

        uri = parsed;
        error = string.Empty;
        return true;
    }
}

/// <summary>
/// Encrypts webhook credentials with ASP.NET Data Protection. Its key ring is
/// outside PostgreSQL, so a database dump alone cannot post to any channel.
/// </summary>
public sealed class DiscordWebhookProtector(IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector =
        protection.CreateProtector("NextSkill.DiscordWebhook.v1");

    public string Protect(Uri uri) => _protector.Protect(uri.AbsoluteUri);

    public bool TryUnprotect(string protectedUrl, out Uri? uri)
    {
        uri = null;
        try
        {
            var value = _protector.Unprotect(protectedUrl);
            return DiscordWebhookUrls.TryParse(value, out uri, out _);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}

public readonly record struct DiscordSendResult(bool Succeeded, string Code, TimeSpan? RetryAfter = null);

public interface IDiscordWebhookTransport
{
    Task<DiscordSendResult> SendAsync(Uri webhook, JsonDocument payload, CancellationToken cancellationToken);
}

/// <summary>
/// A deliberately unlogged HTTP transport. IHttpClientFactory's default
/// logging includes request URIs, and a Discord webhook URI is itself a secret.
/// </summary>
public sealed class DiscordWebhookTransport : IDiscordWebhookTransport, IDisposable
{
    private readonly HttpClient _http = new(CreateHandler())
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    public async Task<DiscordSendResult> SendAsync(
        Uri webhook,
        JsonDocument payload,
        CancellationToken cancellationToken)
    {
        var target = new UriBuilder(webhook) { Query = "wait=true" }.Uri;
        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(
                payload.RootElement.GetRawText(), Encoding.UTF8, "application/json"),
        };

        try
        {
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
                return new(true, "ok");

            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryAt)
                retryAfter = retryAt - DateTimeOffset.UtcNow;

            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            return new(false, $"http_{(int)response.StatusCode}", retryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "timeout");
        }
        catch (HttpRequestException)
        {
            return new(false, "network_error");
        }
    }

    public void Dispose() => _http.Dispose();

    private static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectPublicAsync,
    };

    private static async ValueTask<Stream> ConnectPublicAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(
            context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("Destination did not resolve exclusively to public addresses.");

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                last = ex;
                if (cancellationToken.IsCancellationRequested) throw;
            }
        }

        throw new HttpRequestException("No public destination address accepted the connection.", last);
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None)) return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                return false;
            // fc00::/7 — IPv6 unique-local space.
            return (bytes[0] & 0xfe) != 0xfc;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var a = bytes[0];
        var b = bytes[1];

        return !(a == 0 || a == 10 || a == 127 ||
                 (a == 100 && b is >= 64 and <= 127) ||
                 (a == 169 && b == 254) ||
                 (a == 172 && b is >= 16 and <= 31) ||
                 (a == 192 && b == 168) ||
                 (a == 192 && b == 0) ||
                 (a == 198 && b is 18 or 19) ||
                 (a == 198 && b == 51 && bytes[2] == 100) ||
                 (a == 203 && b == 0 && bytes[2] == 113) ||
                 a >= 224);
    }
}
