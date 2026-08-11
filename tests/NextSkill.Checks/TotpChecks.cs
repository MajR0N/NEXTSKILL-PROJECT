using System.Text;
using NextSkill.Domain.Auth;

namespace NextSkill.Checks;

/// <summary>
/// TOTP against the published RFC 6238 vectors (decision 047).
///
/// The same discipline as Glicko-2: an implementation checked only against our
/// own expectations proves that the code agrees with itself. Checked against the
/// RFC, it proves that <b>Authy will accept it</b> — which is the actual
/// requirement, and not something we can verify by reasoning.
/// </summary>
public static class TotpChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        // RFC 6238 Appendix B: the SHA-1 seed is the ASCII "12345678901234567890".
        var rfcSecret = Totp.Base32Encode(Encoding.ASCII.GetBytes("12345678901234567890"));

        check("TOTP: reproduces the RFC 6238 test vectors exactly", () =>
        {
            // If any of these drift, the implementation is wrong no matter how
            // reasonable everything else looks — and every authenticator app in
            // the world would reject our codes.
            var vectors = new (long Time, string Code)[]
            {
                (59,          "287082"),
                (1111111109,  "081804"),
                (1111111111,  "050471"),
                (1234567890,  "005924"),
                (2000000000,  "279037"),
                (20000000000, "353130"),
            };

            foreach (var (time, expected) in vectors)
            {
                var actual = Totp.Compute(rfcSecret, DateTimeOffset.FromUnixTimeSeconds(time));
                that(actual == expected, $"t={time}: expected {expected}, got {actual}");
            }
        });

        check("TOTP: a code is accepted around its own step and rejected far away", () =>
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(1111111109);
            var code = Totp.Compute(rfcSecret, at);

            that(Totp.Verify(rfcSecret, code, at), "the current code must be accepted");

            // ±1 step, because phone clocks drift and people type slowly.
            that(Totp.Verify(rfcSecret, code, at.AddSeconds(-Totp.StepSeconds)), "one step early must be accepted");
            that(Totp.Verify(rfcSecret, code, at.AddSeconds(Totp.StepSeconds)), "one step late must be accepted");

            that(!Totp.Verify(rfcSecret, code, at.AddSeconds(Totp.StepSeconds * 3)),
                "a code from two minutes ago must NOT still work — the window is the exposure");
            that(!Totp.Verify(rfcSecret, code, at.AddHours(-1)), "an hour early must be rejected");
        });

        check("TOTP: malformed input is refused rather than crashing", () =>
        {
            var at = DateTimeOffset.UtcNow;

            foreach (var junk in new[] { "", "   ", "abcdef", "12345", "1234567", "12 34 56 78" })
                that(!Totp.Verify(rfcSecret, junk, at), $"'{junk}' must be rejected");

            // A corrupt stored secret must fail the login, not 500 it — the same
            // rule the password verifier follows.
            that(!Totp.Verify("not!base32!", "123456", at), "an invalid secret must return false, not throw");
            that(!Totp.Verify("", "123456", at), "an empty secret must return false");
        });

        check("TOTP: a fresh secret is 160 bits and Base32 round-trips", () =>
        {
            var secret = Totp.NewSecret();
            that(Totp.Base32Decode(secret).Length == 20, "RFC 4226 wants at least 128 bits; 160 is the norm");
            that(Totp.Base32Encode(Totp.Base32Decode(secret)) == secret, "encoding must round-trip");

            // Two secrets in a row must differ, or every account shares one.
            that(Totp.NewSecret() != Totp.NewSecret(), "secrets must be random per account");
        });

        check("TOTP: the provisioning URI is what an authenticator expects", () =>
        {
            var secret = Totp.NewSecret();
            var uri = Totp.ProvisioningUri("NEXTSKILL", "pukawka@example.com", secret);

            that(uri.StartsWith("otpauth://totp/"), "apps only recognise the otpauth scheme");
            that(uri.Contains($"secret={secret}"), "the secret must be present and unmangled");
            that(uri.Contains("issuer=NEXTSKILL"),
                "without the issuer parameter the entry shows up in the app as 'Unknown'");
            that(uri.Contains("digits=6") && uri.Contains("period=30"), "the defaults must be explicit");
        });
    }
}
