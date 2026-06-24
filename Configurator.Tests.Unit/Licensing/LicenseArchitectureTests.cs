using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseArchitectureTests
{
    [Fact]
    public void Repository_DoesNotContainCommittedPrivateKeysOrLicenseArtifacts()
    {
        var root = FindRepositoryRoot();
        var trackedLikeFiles = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && IsTextFile(path));
        var forbiddenExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pem",
            ".promlicense",
            ".promrequest"
        };

        foreach (var file in trackedLikeFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain(PrivateKeyMarker(ecKey: true), text, StringComparison.Ordinal);
            Assert.DoesNotContain(PrivateKeyMarker(ecKey: false), text, StringComparison.Ordinal);
            Assert.False(forbiddenExtensions.Contains(Path.GetExtension(file)), file);
        }
    }

    private static string PrivateKeyMarker(bool ecKey)
    {
        var begin = string.Concat("-----", "BEGIN");
        var privateKey = string.Concat("PRIVATE ", "KEY-----");

        return ecKey ? $"{begin} EC {privateKey}" : $"{begin} {privateKey}";
    }

    private static bool IsTextFile(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".cs"
            or ".json"
            or ".md"
            or ".csproj"
            or ".slnx"
            or ".txt"
            or ".gitignore";

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
}
