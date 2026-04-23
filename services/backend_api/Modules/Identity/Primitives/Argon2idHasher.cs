using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace BackendApi.Modules.Identity.Primitives;

public sealed class Argon2idHasher
{
    private const string Prefix = "$argon2id$v=19$";
    private const int SaltLength = 16;
    private const int HashLength = 32;

    private static readonly Argon2Parameters CustomerParams = new(
        MemorySizeKb: 64 * 1024,
        Iterations: 3,
        DegreeOfParallelism: 2);

    private static readonly Argon2Parameters AdminParams = new(
        MemorySizeKb: 96 * 1024,
        Iterations: 4,
        DegreeOfParallelism: 2);

    public string HashPassword(string password, SurfaceKind surface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var parameters = CurrentParams(surface);
        var hash = Derive(password, salt, parameters);
        return EncodeHash(hash, salt, parameters);
    }

    public Argon2VerificationResult VerifyAndRehashIfNeeded(
        string password,
        string encodedHash,
        SurfaceKind surface)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedHash);

        if (!TryParseHash(encodedHash, out var parameters, out var salt, out var expectedHash))
        {
            return Argon2VerificationResult.Invalid;
        }

        var actualHash = Derive(password, salt, parameters);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            return Argon2VerificationResult.Invalid;
        }

        var target = CurrentParams(surface);
        if (parameters.IsAtLeast(target))
        {
            return new Argon2VerificationResult(true, false, null);
        }

        var refreshedHash = HashPassword(password, surface);
        return new Argon2VerificationResult(true, true, refreshedHash);
    }

    private static Argon2Parameters CurrentParams(SurfaceKind surface) =>
        surface == SurfaceKind.Admin ? AdminParams : CustomerParams;

    private static byte[] Derive(string password, byte[] salt, Argon2Parameters parameters)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = parameters.Iterations,
            DegreeOfParallelism = parameters.DegreeOfParallelism,
            MemorySize = parameters.MemorySizeKb,
        };

        return argon2.GetBytes(HashLength);
    }

    private static string EncodeHash(byte[] hash, byte[] salt, Argon2Parameters parameters)
    {
        var saltBase64 = Convert.ToBase64String(salt);
        var hashBase64 = Convert.ToBase64String(hash);
        return $"{Prefix}m={parameters.MemorySizeKb},t={parameters.Iterations},p={parameters.DegreeOfParallelism}${saltBase64}${hashBase64}";
    }

    private static bool TryParseHash(
        string encodedHash,
        out Argon2Parameters parameters,
        out byte[] salt,
        out byte[] hash)
    {
        parameters = default;
        salt = [];
        hash = [];

        if (!encodedHash.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = encodedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 5)
        {
            return false;
        }

        var metadata = segments[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (metadata.Length != 3)
        {
            return false;
        }

        try
        {
            var memory = ParseInt(metadata[0], "m=");
            var iterations = ParseInt(metadata[1], "t=");
            var parallelism = ParseInt(metadata[2], "p=");
            parameters = new Argon2Parameters(memory, iterations, parallelism);

            salt = Convert.FromBase64String(segments[3]);
            hash = Convert.FromBase64String(segments[4]);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int ParseInt(string raw, string prefix)
    {
        if (!raw.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"Expected prefix '{prefix}'.");
        }

        return int.Parse(raw[prefix.Length..], CultureInfo.InvariantCulture);
    }

    private readonly record struct Argon2Parameters(
        int MemorySizeKb,
        int Iterations,
        int DegreeOfParallelism)
    {
        public bool IsAtLeast(Argon2Parameters other) =>
            MemorySizeKb >= other.MemorySizeKb
            && Iterations >= other.Iterations
            && DegreeOfParallelism >= other.DegreeOfParallelism;
    }
}

public sealed record Argon2VerificationResult(
    bool IsValid,
    bool NeedsRehash,
    string? RehashedHash)
{
    public static Argon2VerificationResult Invalid { get; } = new(false, false, null);
}
