using Configurator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class AppConfigServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AtomicSave_FailureOrCancellationBeforeReplace_PreservesBytesAndDoesNotReload(bool multiple, bool cancel)
    {
        using var files = new SettingsFiles();
        var configuration = new ReloadCountingConfiguration();
        var service = files.Create(configuration);
        using var cancellation = new CancellationTokenSource();
        service.BeforeSettingsReplace = (temporary, target) =>
        {
            Assert.Equal(files.Path, target);
            Assert.Equal(files.Original, File.ReadAllBytes(target));
            Assert.NotNull(JsonNode.Parse(File.ReadAllText(temporary))!["Updated"]);
            Assert.Equal(0, configuration.ReloadCount);
            if (cancel) cancellation.Cancel();
            else throw new IOException("Injected replacement failure");
        };

        Task Save() => multiple
            ? service.SaveSectionsAsync(new Dictionary<string, object> { ["Updated"] = new { Value = 2 } }, cancellation.Token)
            : service.SaveSectionAsync("Updated", new { Value = 2 }, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(Save);
        else Assert.Contains("replacement", (await Assert.ThrowsAsync<IOException>(Save)).Message);

        Assert.Equal(files.Original, File.ReadAllBytes(files.Path));
        Assert.Equal(0, configuration.ReloadCount);
        Assert.Empty(Directory.GetFiles(files.Directory, "*.tmp"));
        using var reopened = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(files.Directory).AddJsonFile("appsettings.json").Build();
        Assert.Equal("42", reopened["Unknown:Value"]);
        Assert.Null(reopened["Updated:Value"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AtomicSave_CommitsAllSectionsBeforeReload_AndPreservesOtherSections(bool multiple)
    {
        using var files = new SettingsFiles();
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().SetBasePath(files.Directory).AddJsonFile("appsettings.json").Build();
        var service = files.Create(configuration);
        var reloads = 0;
        using var subscription = configuration.GetReloadToken().RegisterChangeCallback(_ =>
        {
            var saved = JsonNode.Parse(File.ReadAllText(files.Path))!;
            Assert.Equal(2, saved["Updated"]!["Value"]!.GetValue<int>());
            if (multiple) Assert.Equal(3, saved["Second"]!["Value"]!.GetValue<int>());
            reloads++;
        }, null);
        service.BeforeSettingsReplace = (_, _) => Assert.Equal(0, reloads);
        if (multiple)
            await service.SaveSectionsAsync(new Dictionary<string, object>
            {
                ["Updated"] = new { Value = 2 }, ["Second"] = new { Value = 3 }
            });
        else await service.SaveSectionAsync("Updated", new { Value = 2 });

        Assert.Equal(1, reloads);
        Assert.Equal("2", configuration["Updated:Value"]);
        var result = JsonNode.Parse(File.ReadAllText(files.Path))!;
        var original = JsonNode.Parse(files.Original)!;
        foreach (var section in original.AsObject())
            Assert.True(JsonNode.DeepEquals(section.Value, result[section.Key]), section.Key);
    }

    [Fact]
    public async Task AtomicSave_ConcurrentSaves_ReadTheLastCommittedFile()
    {
        using var files = new SettingsFiles();
        var service = files.Create(new ReloadCountingConfiguration());
        var firstAtReplace = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var replacements = 0;
        service.BeforeSettingsReplace = (_, _) =>
        {
            if (Interlocked.Increment(ref replacements) == 1)
            {
                firstAtReplace.SetResult();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10)), "Test did not release the first save");
            }
        };
        var first = Task.Run(() => service.SaveSectionAsync("First", new { Value = 1 }));
        try
        {
            await firstAtReplace.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var second = service.SaveSectionsAsync(new Dictionary<string, object> { ["Second"] = new { Value = 2 } });
            Assert.False(second.IsCompleted);
            release.Set();
            await Task.WhenAll(first, second);
        }
        finally { release.Set(); await first; }
        var saved = JsonNode.Parse(File.ReadAllText(files.Path))!;
        Assert.Equal(1, saved["First"]!["Value"]!.GetValue<int>());
        Assert.Equal(2, saved["Second"]!["Value"]!.GetValue<int>());
        Assert.Equal(2, replacements);
    }

    [WindowsFact]
    public async Task AtomicSave_FileMoveDeniedByWindowsHandle_PreservesTargetAndDiagnosesFailure()
    {
        using var files = new SettingsFiles();
        var configuration = new ReloadCountingConfiguration();
        var service = files.Create(configuration);
        var logger = new SettingsLogger();
        service.Logger = logger;
        using var targetLock = new FileStream(files.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reachedMove = false;
        service.BeforeSettingsReplace = (_, _) => reachedMove = true;
        var error = await Record.ExceptionAsync(() => service.SaveSectionAsync("Updated", new { Value = 2 }));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.True(reachedMove);
        Assert.Equal(files.Original, File.ReadAllBytes(files.Path));
        Assert.Equal(0, configuration.ReloadCount);
        Assert.Contains(logger.Entries, entry => (entry.Exception is IOException or UnauthorizedAccessException) && entry.Message.Contains("atomically"));
        Assert.Empty(Directory.GetFiles(files.Directory, "*.tmp"));
    }

    [Fact]
    public async Task AtomicSave_CleanupFailure_DoesNotMaskCommitFailure()
    {
        using var files = new SettingsFiles();
        var service = files.Create(new ReloadCountingConfiguration());
        var logger = new SettingsLogger();
        service.Logger = logger;
        var originalError = new IOException("Original commit failure");
        service.BeforeSettingsReplace = (temporary, _) =>
        {
            File.Delete(temporary);
            Directory.CreateDirectory(temporary);
            throw originalError;
        };
        var error = await Assert.ThrowsAsync<IOException>(() => service.SaveSectionAsync("Updated", new { Value = 2 }));
        Assert.Same(originalError, error);
        Assert.Equal(files.Original, File.ReadAllBytes(files.Path));
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("temporary") && entry.Exception is not null);
    }

    [Fact]
    public async Task AtomicSave_TemporaryWriteFailure_DoesNotTouchActiveFile()
    {
        using var files = new SettingsFiles();
        var configuration = new ReloadCountingConfiguration();
        var service = files.Create(configuration);
        // A component longer than the filesystem limit rejects the temporary write before commit.
        var longPath = System.IO.Path.Combine(files.Directory, new string('x', 250));
        File.WriteAllBytes(longPath, files.Original);
        service = new AppConfigService(configuration, longPath, files.UserPath, []);
        await Assert.ThrowsAnyAsync<IOException>(() => service.SaveSectionAsync("Updated", new { Value = 2 }));
        Assert.Equal(files.Original, File.ReadAllBytes(longPath));
        Assert.Equal(0, configuration.ReloadCount);
    }

    private sealed class SettingsFiles : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"settings-atomic-{Guid.NewGuid():N}");
        public string Path => System.IO.Path.Combine(Directory, "appsettings.json");
        public string UserPath => System.IO.Path.Combine(Directory, "user_settings.json");
        public byte[] Original { get; } = System.Text.Encoding.UTF8.GetBytes("""
            { "Unknown": { "Value": 42 }, "Modbus": { "vendor": true }, "ModbusDemo": { "vendor": 7 },
              "RouteMapRuntime": { "SignalSource": "Mock" }, "Help": { "Contacts": [] }, "Startup": { "Enabled": false } }
            """);
        public SettingsFiles()
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllBytes(Path, Original);
        }
        public AppConfigService Create(IConfiguration configuration) => new(configuration, Path, UserPath, []);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires Windows delete-sharing semantics; portable failure injection tests run on all platforms.";
        }
    }
}
