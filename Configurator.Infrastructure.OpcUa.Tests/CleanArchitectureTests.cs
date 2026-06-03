using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class CleanArchitectureTests
{
    [Fact]
    public void ApplicationLayer_ShouldNotReferenceOpcUaSdk()
    {
        var root = FindRepositoryRoot();
        var applicationDirectory = Path.Combine(root.FullName, "Configurator.Application");
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        var projectText = File.ReadAllText(Path.Combine(applicationDirectory, "Configurator.Application.csproj"));

        Assert.DoesNotContain("Opc.Ua", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("OPCFoundation", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopLayer_ShouldNotReferenceOpcUaSdk()
    {
        var root = FindRepositoryRoot();
        var desktopDirectory = Path.Combine(root.FullName, "Configurator.Desktop");
        var sourceText = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(desktopDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        var projectText = File.ReadAllText(Path.Combine(desktopDirectory, "Configurator.Desktop.csproj"));

        Assert.DoesNotContain("Opc.Ua", sourceText, StringComparison.Ordinal);
        Assert.DoesNotContain("OPCFoundation", projectText, StringComparison.Ordinal);
    }

    [Fact]
    public void Solution_ShouldContainOpcUaProjects()
    {
        var root = FindRepositoryRoot();
        var solutionText = File.ReadAllText(Path.Combine(root.FullName, "DesktopTemplate.slnx"));

        Assert.Contains("Configurator.Infrastructure.OpcUa/Configurator.Infrastructure.OpcUa.csproj", solutionText, StringComparison.Ordinal);
        Assert.Contains("Configurator.Infrastructure.OpcUa.Tests/Configurator.Infrastructure.OpcUa.Tests.csproj", solutionText, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopTemplate.slnx")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
