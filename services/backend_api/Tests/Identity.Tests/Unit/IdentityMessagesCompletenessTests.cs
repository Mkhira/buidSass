using System.Text.RegularExpressions;
using FluentAssertions;

namespace Identity.Tests.Unit;

public sealed class IdentityMessagesCompletenessTests
{
    [Fact]
    public void ArabicAndEnglishBundles_HaveMatchingKeys()
    {
        var repoRoot = FindRepositoryRoot();
        var messagesDir = Path.Combine(repoRoot, "services", "backend_api", "Modules", "Identity", "Messages");
        var arPath = Path.Combine(messagesDir, "identity.ar.icu");
        var enPath = Path.Combine(messagesDir, "identity.en.icu");

        File.Exists(arPath).Should().BeTrue("Arabic message bundle must exist.");
        File.Exists(enPath).Should().BeTrue("English message bundle must exist.");

        var arKeys = ParseKeys(arPath);
        var enKeys = ParseKeys(enPath);

        arKeys.Should().BeEquivalentTo(enKeys, "AR/EN bundles must stay in sync.");
    }

    [Fact]
    public void AllEmittedIdentityReasonCodes_HaveArabicAndEnglishMessages()
    {
        var repoRoot = FindRepositoryRoot();
        var messagesDir = Path.Combine(repoRoot, "services", "backend_api", "Modules", "Identity", "Messages");
        var arPath = Path.Combine(messagesDir, "identity.ar.icu");
        var enPath = Path.Combine(messagesDir, "identity.en.icu");
        var arKeys = ParseKeys(arPath);
        var enKeys = ParseKeys(enPath);

        var emittedReasonCodes = FindEmittedReasonCodes(repoRoot);

        emittedReasonCodes.Should().NotBeEmpty();
        arKeys.Should().Contain(emittedReasonCodes, "every emitted reason code must exist in Arabic messages.");
        enKeys.Should().Contain(emittedReasonCodes, "every emitted reason code must exist in English messages.");
    }

    private static HashSet<string> ParseKeys(string filePath)
    {
        var pattern = new Regex(@"^\s*([a-zA-Z0-9_.-]+)\s*=", RegexOptions.Compiled);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(filePath))
        {
            var match = pattern.Match(line);
            if (match.Success)
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "services", "backend_api");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test execution directory.");
    }

    private static HashSet<string> FindEmittedReasonCodes(string repoRoot)
    {
        var identityRoot = Path.Combine(repoRoot, "services", "backend_api", "Modules", "Identity");
        var literalPattern = new Regex("\"(identity\\.[a-zA-Z0-9_.-]+)\"", RegexOptions.Compiled);
        var codes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var filePath in Directory.EnumerateFiles(identityRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (filePath.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in literalPattern.Matches(File.ReadAllText(filePath)))
            {
                var code = match.Groups[1].Value;
                if (IsReasonCodeCandidate(code))
                {
                    codes.Add(code);
                }
            }
        }

        return codes;
    }

    private static bool IsReasonCodeCandidate(string code)
    {
        if (!code.StartsWith("identity.", StringComparison.Ordinal))
        {
            return false;
        }

        if (code.StartsWith("identity.admin.", StringComparison.Ordinal) && !code.StartsWith("identity.admin.sessions.", StringComparison.Ordinal))
        {
            return false;
        }

        if (code.StartsWith("identity.customer.", StringComparison.Ordinal)
            || code.StartsWith("identity.permission", StringComparison.Ordinal)
            || code.StartsWith("identity.rate-limit.", StringComparison.Ordinal))
        {
            return false;
        }

        if (code.Equals("identity.authorization", StringComparison.Ordinal)
            || code.Equals("identity.step_up", StringComparison.Ordinal)
            || code.Equals("identity.reference-data", StringComparison.Ordinal)
            || code.Equals("identity.dev-data", StringComparison.Ordinal)
            || code.Equals("identity.refresh_tokens", StringComparison.Ordinal)
            || code.Equals("identity.revoked_refresh_tokens", StringComparison.Ordinal))
        {
            return false;
        }

        if (code.Contains(".ecdsa.", StringComparison.Ordinal) || code.Contains(".secret.", StringComparison.Ordinal))
        {
            return false;
        }

        return code.StartsWith("identity.common.", StringComparison.Ordinal)
            || code.StartsWith("identity.sign_in.", StringComparison.Ordinal)
            || code.StartsWith("identity.lockout.", StringComparison.Ordinal)
            || code.StartsWith("identity.step_up.", StringComparison.Ordinal)
            || code.StartsWith("identity.register.", StringComparison.Ordinal)
            || code.StartsWith("identity.phone.", StringComparison.Ordinal)
            || code.StartsWith("identity.email_verification.", StringComparison.Ordinal)
            || code.StartsWith("identity.otp.", StringComparison.Ordinal)
            || code.StartsWith("identity.email.", StringComparison.Ordinal)
            || code.StartsWith("identity.invitation.", StringComparison.Ordinal)
            || code.StartsWith("identity.partial_auth.", StringComparison.Ordinal)
            || code.StartsWith("identity.account.", StringComparison.Ordinal)
            || code.StartsWith("identity.mfa.", StringComparison.Ordinal)
            || code.StartsWith("identity.password.", StringComparison.Ordinal)
            || code.StartsWith("identity.password_reset.", StringComparison.Ordinal)
            || code.StartsWith("identity.password_change.", StringComparison.Ordinal)
            || code.StartsWith("identity.refresh.", StringComparison.Ordinal)
            || code.StartsWith("identity.sign_out.", StringComparison.Ordinal)
            || code.StartsWith("identity.rate_limit.", StringComparison.Ordinal)
            || code.StartsWith("identity.session.", StringComparison.Ordinal)
            || code.StartsWith("identity.authorization.", StringComparison.Ordinal)
            || code.StartsWith("identity.admin.sessions.", StringComparison.Ordinal);
    }
}
