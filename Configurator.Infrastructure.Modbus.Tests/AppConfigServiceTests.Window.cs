using Configurator.Application.Services;
using Configurator.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Configurator.Infrastructure.Modbus.Tests;

public sealed partial class AppConfigServiceTests
{
    [Theory]
    [InlineData("{\"Width\":")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Width\":\"invalid\"}")]
    [InlineData("{\"Width\":-1}")]
    [InlineData("{\"Height\":0}")]
    [InlineData("{\"Width\":1e999}")]
    [InlineData("{\"Width\":1200,\"Width\":1300,\"Height\":800}")]
    [InlineData("{\"Vendor\":{\"Duplicate\":1,\"Duplicate\":2}}")]
    public void WindowSettings_InvalidFile_UsesDefaultsWithoutChangingOriginal_ThenPreservesDamagedBytes(string json)
    {
        using var files = new SettingsFiles();
        var damaged = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(json)).ToArray();
        File.WriteAllBytes(files.UserPath, damaged);
        var (service, log) = CreateWindowService(files);

        var settings = service.LoadUserSettings();
        Assert.Equal((1340d, 860d, true), (settings.Width, settings.Height, settings.IsFullScreen));
        Assert.Equal(damaged, File.ReadAllBytes(files.UserPath));
        Assert.Empty(Directory.GetFiles(files.Directory, "*.bak"));
        Assert.Contains(log.Entries, entry => entry.Exception is System.Text.Json.JsonException && entry.Message.Contains("defaults"));

        settings.Width = 1450;
        service.SaveUserSettings(settings);
        var backup = Assert.Single(Directory.GetFiles(files.Directory, "user_settings.json.corrupt.*.bak"));
        Assert.Equal(damaged, File.ReadAllBytes(backup));
        var loaded = files.Create(new ConfigurationBuilder().Build()).LoadUserSettings();
        Assert.Equal((1450d, 860d, true), (loaded.Width, loaded.Height, loaded.IsFullScreen));
        service.SaveUserSettings(loaded);
        Assert.Single(Directory.GetFiles(files.Directory, "*.bak"));
    }

    [Fact]
    public void WindowSettings_DuplicateKeysDuringSave_AreBackedUpAndReplaced()
    {
        using var files = new SettingsFiles();
        const string original = "{\"Width\":1200,\"Width\":1300,\"Height\":800}";
        File.WriteAllText(files.UserPath, original);
        var (service, _) = CreateWindowService(files);
        service.SaveUserSettings(new UserSettings { Width = 1500, Height = 900 });
        Assert.Equal(1500, service.LoadUserSettings().Width);
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(files.Directory, "*.bak"))));
    }

    [Fact]
    public void WindowSettings_LockedForReading_UsesDefaultsAndDiagnosesFailure()
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.UserPath, "{}");
        var (service, log) = CreateWindowService(files);
        using (new FileStream(files.UserPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var settings = service.LoadUserSettings();
            Assert.Equal(1340, settings.Width);
            Assert.Contains(log.Entries, entry => entry.Exception is IOException);
            Assert.ThrowsAny<IOException>(() => service.SaveUserSettings(settings));
        }
        Assert.Equal("{}", File.ReadAllText(files.UserPath));
    }

    [WindowsFact]
    public void WindowSettings_FileMoveDeniedByWindowsHandle_PreservesTargetAndDiagnosesFailure()
    {
        using var files = new SettingsFiles();
        const string original = "{\"Width\":1600,\"Height\":900}";
        File.WriteAllText(files.UserPath, original);
        var (service, log) = CreateWindowService(files);
        var settings = service.LoadUserSettings();
        using var targetLock = new FileStream(files.UserPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reachedMove = false;
        service.BeforeSettingsReplace = (_, _) => reachedMove = true;
        var error = Record.Exception(() => service.SaveUserSettings(settings));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.True(reachedMove);
        Assert.Equal(original, File.ReadAllText(files.UserPath));
        Assert.Contains(log.Entries, entry => (entry.Exception is IOException or UnauthorizedAccessException) && entry.Message.Contains("Failed to save"));
        Assert.Empty(Directory.GetFiles(files.Directory, "*.tmp"));
    }

    [Fact]
    public void WindowSettings_DirectoryInsteadOfFile_UsesDefaultsAndSaveFailureIsDiagnosed()
    {
        using var files = new SettingsFiles();
        Directory.CreateDirectory(files.UserPath);
        var (service, log) = CreateWindowService(files);
        var settings = service.LoadUserSettings();
        Assert.Equal(1340, settings.Width);
        Assert.NotNull(Record.Exception(() => service.SaveUserSettings(settings)));
        Assert.True(Directory.Exists(files.UserPath));
        Assert.Contains(log.Entries, entry => entry.Message.Contains("Failed to load") && entry.Exception is not null);
        Assert.Contains(log.Entries, entry => entry.Message.Contains("Failed to save") && entry.Exception is not null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WindowSettings_ReplacementFailure_PreservesValidOrDamagedBytes(bool damaged)
    {
        using var files = new SettingsFiles();
        var original = Encoding.UTF8.GetBytes(damaged ? "{broken" : "{\"Width\":1550,\"Height\":900}");
        File.WriteAllBytes(files.UserPath, original);
        var (service, log) = CreateWindowService(files);
        var settings = service.LoadUserSettings();
        service.BeforeSettingsReplace = (temporary, target) =>
        {
            Assert.Equal(files.UserPath, target);
            Assert.Equal(original, File.ReadAllBytes(target));
            Assert.NotNull(JsonNode.Parse(File.ReadAllText(temporary)));
            throw new UnauthorizedAccessException("Injected window settings replacement failure");
        };
        Assert.Throws<UnauthorizedAccessException>(() => service.SaveUserSettings(settings));
        Assert.Equal(original, File.ReadAllBytes(files.UserPath));
        Assert.Empty(Directory.GetFiles(files.Directory, "*.tmp"));
        Assert.Contains(log.Entries, entry => entry.Exception is UnauthorizedAccessException);
        if (damaged)
        {
            var backup = Assert.Single(Directory.GetFiles(files.Directory, "*.bak"));
            Assert.Equal(original, File.ReadAllBytes(backup));
        }
    }

    [Fact]
    public void WindowSettings_SuccessPreservesUnknownFieldsAndDoesNotReloadRuntimeConfiguration()
    {
        using var files = new SettingsFiles();
        File.WriteAllText(files.UserPath, """{"Width":1600,"Height":900,"IsFullScreen":false,"Vendor":{"Nested":[1,2]}}""");
        var config = new ReloadCountingConfiguration();
        var service = files.Create(config);
        var settings = service.LoadUserSettings();
        Assert.Equal((1600d, 900d, false), (settings.Width, settings.Height, settings.IsFullScreen));
        settings.Width = 1700;
        service.SaveUserSettings(settings);
        Assert.Equal(0, config.ReloadCount);
        Assert.Equal(1700, service.LoadUserSettings().Width);
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(files.UserPath))!["Vendor"]!["Nested"]![1]!.GetValue<int>());
    }

    [Fact]
    public void WindowSettings_LegacyMigrationFailure_StillReturnsLegacySettingsAndKeepsSource()
    {
        using var files = new SettingsFiles();
        var legacy = Path.Combine(files.Directory, "legacy.json");
        const string original = "{\"Width\":1600,\"Height\":1000,\"IsFullScreen\":true}";
        File.WriteAllText(legacy, original);
        var service = new AppConfigService(new ConfigurationBuilder().Build(), files.Path, files.UserPath, [legacy]);
        var log = new SettingsLogger();
        service.Logger = log;
        service.BeforeSettingsReplace = (_, _) => throw new IOException("Migration replacement failed");
        var actual = service.LoadUserSettings();
        Assert.Equal((1600d, 1000d, true), (actual.Width, actual.Height, actual.IsFullScreen));
        Assert.Equal(original, File.ReadAllText(legacy));
        Assert.False(File.Exists(files.UserPath));
        Assert.Contains(log.Entries, entry => entry.Message.Contains("Could not migrate"));
    }

    private static (AppConfigService Service, SettingsLogger Log) CreateWindowService(SettingsFiles files)
    {
        var defaults = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WindowSettings:Width"] = "1340", ["WindowSettings:Height"] = "860", ["WindowSettings:IsFullScreen"] = "true"
        }).Build();
        var service = files.Create(defaults);
        var log = new SettingsLogger();
        service.Logger = log;
        return (service, log);
    }

    private sealed class SettingsLogger : ILogger<AppConfigService>
    {
        public List<(string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((formatter(state, exception), exception));
    }
}
