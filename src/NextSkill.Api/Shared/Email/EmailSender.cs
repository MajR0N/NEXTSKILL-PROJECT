using System.Text;

namespace NextSkill.Api.Shared.Email;

/// <summary>
/// Sending an email (decisions 011, 043).
///
/// One interface with one implementation is usually the abstraction to avoid —
/// this one earns its place because the second implementation is inevitable and
/// the failure mode of getting it wrong is silent. A reset flow whose mail
/// vanishes looks identical, from the outside, to a reset flow that works.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// Development sender: writes each message to a file instead of sending it.
///
/// Deliberately <b>not</b> a no-op logger. A file you can open is what makes the
/// reset link followable in development, and it is what lets the checks read a
/// real token out of a real message rather than reaching into the database and
/// proving nothing about the email path.
/// </summary>
public sealed class FileEmailSender(string directory, ILogger<FileEmailSender> log) : IEmailSender
{
    /// <summary>Where messages land. The checks read from the same place.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Path.GetTempPath(), "nextskill-mail");

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}.eml");
        var content = new StringBuilder()
            .AppendLine($"To: {to}")
            .AppendLine($"Subject: {subject}")
            .AppendLine($"Date: {DateTimeOffset.UtcNow:R}")
            .AppendLine()
            .AppendLine(body)
            .ToString();

        await File.WriteAllTextAsync(path, content, ct);

        // The path, not the contents. A reset link in the application log is a
        // credential in the application log (012).
        log.LogInformation("Development email written to {Path}", path);
    }
}
