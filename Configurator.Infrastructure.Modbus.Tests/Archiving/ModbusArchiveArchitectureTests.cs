using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests.Archiving;

public sealed class ModbusArchiveArchitectureTests
{
    [Fact]
    public void ModbusArchive_DoesNotReferenceAvaloniaReactiveUiDesktopViewModelsPersistenceOrSqlite()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "Configurator.Infrastructure.Modbus", "Archiving");
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
        var forbidden = new[]
        {
            "Avalonia",
            "ReactiveUI",
            "Configurator.Desktop",
            "ViewModel",
            "Configurator.Infrastructure.Persistence",
            "Microsoft.Data.Sqlite",
            "Sqlite",
            "ExecuteNonQuery",
            "ExecuteReader",
            "Dispatcher"
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
    public void ModbusArchive_CallbackCode_DoesNotUseWaitResultOrDispatcher()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "Configurator.Infrastructure.Modbus", "Archiving");
        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories);
        var forbidden = new[]
        {
            ".Wait(",
            ".Result",
            "Thread.Sleep",
            "Task.Delay("
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
    public void SensitiveMaterialScan_ModbusArchiveFiles_NoMatches()
    {
        var directories = new[]
        {
            Path.Combine(FindRepositoryRoot(), "Configurator.Infrastructure.Modbus", "Archiving"),
            Path.Combine(FindRepositoryRoot(), "Configurator.Infrastructure.Modbus.Tests", "Archiving")
        };
        var files = directories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));
        var forbidden = new[]
        {
            string.Concat("pass", "word"),
            string.Concat("se", "cret"),
            string.Concat("private", " ", "key"),
            string.Concat("prom", "license")
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var value in forbidden)
            {
                Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
            }
        }
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
