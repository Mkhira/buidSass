using System.Diagnostics;
using FluentAssertions;

namespace Identity.Tests.Integration;

public sealed class OperationalScriptsTests
{
    [Fact]
    public void ScanPlaintextSecretsScript_CompletesSuccessfully()
    {
        var result = RunScript("scripts/dev/scan-plaintext-secrets.sh");
        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public void IdentityAuditSpotCheckScript_CompletesSuccessfully()
    {
        var result = RunScript("scripts/dev/identity-audit-spot-check.sh");
        result.ExitCode.Should().Be(0, result.Output);
    }

    private static ScriptResult RunScript(string relativePath)
    {
        var repoRoot = FindRepositoryRoot();
        var scriptPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = scriptPath,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
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

    private sealed record ScriptResult(int ExitCode, string Output);
}
