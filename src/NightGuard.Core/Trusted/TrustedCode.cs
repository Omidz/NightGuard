using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NightGuard.Core.Trusted;

/// <summary>trusted.json: a salted PBKDF2 hash of the trusted person's 8-digit code. Never the code itself.</summary>
public sealed class TrustedRecord
{
    public bool Enabled { get; set; } = true;
    public string Contact { get; set; } = "";
    public string Alg { get; set; } = "SHA256";
    public int Iterations { get; set; } = 300_000;
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

/// <summary>state.json: wrong-guess counter and lockout.</summary>
public sealed class CodeAttemptState
{
    public int FailedAttempts { get; set; }
    public string? LockedUntil { get; set; }
}

public static partial class TrustedCode
{
    public const int Iterations = 300_000;

    [GeneratedRegex(@"^\d{8}$")] private static partial Regex EightDigits();
    [GeneratedRegex(@"^(\d)\1{7}$")] private static partial Regex AllSame();
    [GeneratedRegex(@"^(\d\d)\1{3}$|^(\d{4})\2$")] private static partial Regex Repeating();
    [GeneratedRegex(@"^(\d)\1(\d)\2(\d)\3(\d)\4$")] private static partial Regex Doubled();
    [GeneratedRegex(@"^(19|20)\d\d(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])$|^(0[1-9]|[12]\d|3[01])(0[1-9]|1[0-2])(19|20)\d\d$|^(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01])(19|20)\d\d$")]
    private static partial Regex LooksLikeDate();

    /// <summary>Why a new code is too weak or malformed, or null if it's fine.</summary>
    public static string? Problem(string code)
    {
        if (!EightDigits().IsMatch(code)) return "The code must be exactly 8 digits.";
        if (AllSame().IsMatch(code)) return "All the same digit is too easy to guess.";
        bool up = true, down = true;
        for (var i = 1; i < 8; i++)
        {
            int a = code[i - 1] - '0', b = code[i] - '0';
            if (b != (a + 1) % 10) up = false;
            if (b != (a + 9) % 10) down = false;
        }
        if (up || down) return "Counting up or down (like 12345678) is too easy to guess.";
        if (Repeating().IsMatch(code)) return "A repeating pattern (like 12121212) is too easy to guess.";
        if (Doubled().IsMatch(code)) return "A doubled pattern (like 11223344) is too easy to guess.";
        if (code.Distinct().Count() <= 2) return "Use at least 3 different digits.";
        if (LooksLikeDate().IsMatch(code)) return "That looks like a date (a birthday is easy to guess). Pick something else.";
        return null;
    }

    public static string Hash(string code, byte[] salt, int iterations, string alg)
    {
        var norm = new string(code.Where(char.IsAsciiDigit).ToArray());
        var name = alg == "SHA1" ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256;
        var bytes = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(norm), salt, iterations, name, 32);
        return Convert.ToBase64String(bytes);
    }

    public static TrustedRecord Create(string code, string contact, DateTime now)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new TrustedRecord
        {
            Enabled = true, Contact = contact.Trim(), Alg = "SHA256", Iterations = Iterations,
            Salt = Convert.ToBase64String(salt), Hash = Hash(code, salt, Iterations, "SHA256"),
            CreatedAt = now.ToString("s", System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static bool Matches(TrustedRecord t, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        byte[] salt;
        try { salt = Convert.FromBase64String(t.Salt); } catch (FormatException) { return false; }
        var iterations = t.Iterations is > 0 and <= 10_000_000 ? t.Iterations : Iterations;
        var got = Encoding.ASCII.GetBytes(Hash(code, salt, iterations, t.Alg));
        var want = Encoding.ASCII.GetBytes(t.Hash);
        return CryptographicOperations.FixedTimeEquals(got, want);
    }

    /// <summary>After 5 wrong codes: 15 minutes, doubling with each further wrong code, up to 24 hours.</summary>
    public static TimeSpan? LockoutAfter(int failedAttempts) =>
        failedAttempts < 5 ? null : TimeSpan.FromMinutes(Math.Min(1440, 15 * Math.Pow(2, failedAttempts - 5)));
}
