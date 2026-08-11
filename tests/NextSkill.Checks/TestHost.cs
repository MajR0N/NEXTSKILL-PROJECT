using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NextSkill.Api.Shared.Persistence;

namespace NextSkill.Checks;

/// <summary>
/// Shared plumbing for the end-to-end checks (decision 034).
///
/// This started as four copies of the same three helpers. It became one file
/// when the rate limiter was changed to a token bucket and <b>every suite broke
/// at once</b> — which is the usual sign that a copied helper was never a
/// helper, only a delay.
/// </summary>
public static class TestHost
{
    public static WebApplicationFactory<NextSkillDb> Create(Action<IWebHostBuilder>? configure = null) =>
        new WebApplicationFactory<NextSkillDb>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development")
             .UseContentRoot(ApiContentRoot)
             .ConfigureServices(s => s.AddSingleton<IStartupFilter, ClientIpFilter>());

            configure?.Invoke(b);
        });

    private static int _clientNumber;

    /// <summary>
    /// A cookie-keeping client that does not chase redirects, speaking HTTPS
    /// and carrying its own source address.
    ///
    /// <b>HTTPS because the session cookie is Secure</b> (011): an http client
    /// would accept the cookie and never send it back, and the tests would fail
    /// for a reason that does not exist in production.
    ///
    /// <b>Its own address because the login limiter is real</b> (011). In a test
    /// host every request arrives with no address at all, so the whole suite
    /// shared one bucket and later checks were 429'd by our own brute-force
    /// defence. Distinct addresses are not a workaround — they are what several
    /// people using the site actually looks like.
    /// </summary>
    public static HttpClient NewClient(this WebApplicationFactory<NextSkillDb> factory)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

        http.DefaultRequestHeaders.Add(
            ClientIpFilter.Header, Interlocked.Increment(ref _clientNumber).ToString());

        return http;
    }

    /// <summary>
    /// Posts a form the way a browser would.
    ///
    /// Razor Pages refuses a POST without an antiforgery token, so the token is
    /// read off a real rendered page first. <paramref name="tokenPage"/> exists
    /// for forms that live somewhere other than their target — logout renders in
    /// the layout, and GETting its route deliberately redirects.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient http, string path, Dictionary<string, string> fields, string? tokenPage = null)
    {
        var page = await http.GetAsync(tokenPage ?? path);
        fields["__RequestVerificationToken"] = AntiforgeryToken(await page.Content.ReadAsStringAsync());
        return await http.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    public static string AntiforgeryToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\"";
        var at = html.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) throw new Exception("No antiforgery token in the page — Razor Pages should always emit one.");

        var valueAt = html.IndexOf("value=\"", at, StringComparison.Ordinal) + 7;
        return html[valueAt..html.IndexOf('"', valueAt)];
    }

    /// <summary>Walks up from the test binaries to find src/NextSkill.Api.</summary>
    public static string ApiContentRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "NextSkill.Api");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate src/NextSkill.Api from " + AppContext.BaseDirectory);
        }
    }

    /// <summary>Stamps each request with a distinct source address before anything else runs.</summary>
    private sealed class ClientIpFilter : IStartupFilter
    {
        public const string Header = "X-Test-Client";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, proceed) =>
            {
                if (ctx.Request.Headers.TryGetValue(Header, out var value) && int.TryParse(value, out var n))
                    ctx.Connection.RemoteIpAddress = IPAddress.Parse($"10.{n / 65536 % 256}.{n / 256 % 256}.{n % 256}");

                await proceed();
            });

            next(app);
        };
    }
}
