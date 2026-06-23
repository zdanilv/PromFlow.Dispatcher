using System.Text.RegularExpressions;
using Xunit;

namespace Configurator.Infrastructure.Persistence.Tests.Architecture;

public sealed class PersistenceArchitectureTests
{
    [Fact]
    public void PersistenceProject_DoesNotReferenceDesktopAvaloniaReactiveUiOrRuntimeProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "Configurator.Infrastructure.Persistence",
            "Configurator.Infrastructure.Persistence.csproj");
        var projectText = File.ReadAllText(projectPath);

        Assert.Contains("Configurator.Application", projectText);
        Assert.DoesNotContain("Configurator.Desktop", projectText);
        Assert.DoesNotContain("Avalonia", projectText);
        Assert.DoesNotContain("ReactiveUI", projectText);
        Assert.DoesNotContain("Configurator.Infrastructure.Modbus", projectText);
        Assert.DoesNotContain("Configurator.Infrastructure.OpcUa", projectText);
    }

    [Fact]
    public void SensitiveMaterialScan_NewPersistenceFiles_NoMatches()
    {
        var repositoryRoot = FindRepositoryRoot();
        var files = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "Configurator.Infrastructure.Persistence"), "*.*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "Configurator.Infrastructure.Persistence.Tests"), "*.*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var plainNeedles = new[]
        {
            string.Concat("pass", "word"),
            string.Concat("se", "cret"),
            string.Concat("private", " ", "key"),
            string.Concat("prom", "license")
        };
        var expressionNeedles = new[]
        {
            new Regex(
                string.Concat(
                    new string(['B', 'E', 'G', 'I', 'N']),
                    " ",
                    ".",
                    "*",
                    new string(['P', 'R', 'I', 'V', 'A', 'T', 'E'])),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var needle in plainNeedles)
            {
                Assert.DoesNotContain(needle, text, StringComparison.OrdinalIgnoreCase);
            }

            foreach (var expression in expressionNeedles)
            {
                Assert.False(expression.IsMatch(text), file);
            }
        }
    }

    [Fact]
    public void PersistenceArchive_DoesNotUseBinaryFormatterWaitResultOrGlobalDropPolicy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var archiveDirectories = new[]
        {
            Path.Combine(repositoryRoot, "Configurator.Infrastructure.Persistence", "Archive"),
            Path.Combine(repositoryRoot, "Configurator.Infrastructure.Persistence.Tests", "Archive")
        };
        var files = archiveDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));
        var forbidden = new[]
        {
            string.Concat("Binary", "Formatter"),
            string.Concat("Bit", "Converter"),
            ".Wait(",
            ".Result",
            string.Concat("BoundedChannelFullMode.", "DropOldest"),
            string.Concat("BoundedChannelFullMode.", "DropNewest")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void PersistenceDependencyInjection_RegistersStage4ArchiveServices()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dependencyInjectionPath = Path.Combine(
            repositoryRoot,
            "Configurator.Infrastructure.Persistence",
            "DependencyInjection.cs");
        var text = File.ReadAllText(dependencyInjectionPath);

        Assert.Contains("ArchiveOptionsValidator", text);
        Assert.Contains("ArchivePriorityBuffer", text);
        Assert.Contains("ArchiveBackoffPolicy", text);
        Assert.Contains("IArchiveHealthService", text);
        Assert.Contains("IArchiveIngestor", text);
        Assert.Contains("SqliteArchiveWriter", text);
        Assert.Contains("IArchiveRuntime", text);
    }

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
