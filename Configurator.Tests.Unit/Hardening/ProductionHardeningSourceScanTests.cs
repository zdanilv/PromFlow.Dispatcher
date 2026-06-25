using System.Text.RegularExpressions;
using Xunit;

namespace Configurator.Tests.Unit.Hardening;

public sealed partial class ProductionHardeningSourceScanTests
{
    [Fact]
    public void ProductionCode_DoesNotContainBlockingCallsOrSecretBypasses()
    {
        var root = FindRepositoryRoot();
        var productionRoots = new[]
        {
            "Configurator.Application",
            "Configurator.Infrastructure",
            "Configurator.Infrastructure.Persistence",
            "Configurator.Infrastructure.Modbus",
            Path.Combine("Configurator.Desktop", "Main"),
            Path.Combine("Configurator.Desktop", "Runtime"),
            Path.Combine("Configurator.Desktop", "Workspace"),
            "Configurator.Boot"
        };
        var failures = productionRoots
            .Select(path => Path.Combine(root, path))
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories))
            .Where(IsTextFile)
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                var relativePath = Path.GetRelativePath(root, path);

                return BlockingSourcePattern()
                    .Matches(text)
                    .Concat(SecretOrBypassPattern().Matches(text))
                    .Select(match => $"{relativePath}: {match.Value}");
            })
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void Repository_DoesNotContainGeneratedLicenseOrPrivateKeyArtifacts()
    {
        var root = FindRepositoryRoot();
        var forbiddenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pem",
            ".promlicense",
            ".promrequest"
        };
        var failures = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => forbiddenExtensions.Contains(Path.GetExtension(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(failures);
    }

    private static bool IsTextFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".json" or ".axaml" or ".csproj";

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DesktopTemplate.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    [GeneratedRegex("BinaryFormatter|\\.Wait\\(|\\.Result|Thread\\.Sleep", RegexOptions.CultureInvariant)]
    private static partial Regex BlockingSourcePattern();

    [GeneratedRegex(
        "AllowTestKeys\\s*=\\s*true|BEGIN .*PRIVATE|PRIVATE KEY-----|debug bypass|hardcoded password",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretOrBypassPattern();
}
