namespace NextSkill.Api.Shared.Sms;

/// <summary>
/// Sending a text message (decision 047).
///
/// <b>There is no gateway yet</b>, and there cannot be one without an account
/// and a bill. The seam exists so the SMS flow is real end to end and the
/// delivery adapter is the only missing piece — and so production
/// <b>refuses to start</b> without one, rather than silently dropping the
/// second factor for everyone who chose it.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken ct = default);
}

/// <summary>Development sender: writes the message to a file, like the email one.</summary>
public sealed class FileSmsSender(string directory, ILogger<FileSmsSender> log) : ISmsSender
{
    public static string DefaultDirectory => Path.Combine(Path.GetTempPath(), "nextskill-sms");

    public async Task SendAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.txt");
        await File.WriteAllTextAsync(path, $"To: {phoneNumber}\n\n{message}\n", ct);

        // The path, never the code. A one-time code in the log is a credential
        // in the log (012).
        log.LogInformation("Development SMS written to {Path}", path);
    }
}
