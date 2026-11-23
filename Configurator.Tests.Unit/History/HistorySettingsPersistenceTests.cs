using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Configurator.Application.Services;
using Configurator.Application.Services.History;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Infrastructure.Services;
using Configurator.Infrastructure.Services.History;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.History;

public sealed class HistorySettingsPersistenceTests
{
    [Fact]
    public async Task Settings_command_saves_draft_and_creates_a_real_zip_when_schedule_is_disabled()
    {
        ReactiveUiTestSetup.EnsureInitialized();
        var directory = Directory.CreateTempSubdirectory("promflow-history-manual-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "appsettings.json");
            var config = new AppConfigService(new ConfigurationBuilder().Build(), configPath, Path.Combine(directory.FullName, "user_settings.json"));
            var store = new SqliteHistoryEventStore(Path.Combine(directory.FullName, "events.db"));
            await using var writer = new HistoryEventWriter(store);
            var options = new HistoryArchiveOptions { Enabled = false, Directory = directory.FullName, MinimumFreeSpaceMb = 0 };
            var service = new HistoryArchiveService(store, writer, options, NullLogger<HistoryArchiveService>.Instance);
            await service.StartAsync();
            try
            {
                using var settings = new HistoryArchiveSettingsViewModel(config, service, Options.Create(new ApplicationOptions()));
                settings.FileNameTemplate = "history_{seq}.zip";
                await settings.SaveCommand.Execute().ToTask();
                Assert.Null(settings.ValidationError);
                Assert.False(service.CurrentOptions.Enabled);
                Assert.Equal(settings.FileNameTemplate, service.CurrentOptions.FileNameTemplate);

                var journal = new RouteMapSessionJournal(writer);
                journal.RecordAlarmActivated(new ModbusAlarmOptions { Id = "alarm.fixture" }, DateTimeOffset.UtcNow);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                do
                {
                    await settings.ArchiveNowCommand.Execute().ToTask();
                    if (settings.Message == "Архивация уже выполняется.")
                        await Task.Delay(10, timeout.Token);
                } while (settings.Message == "Архивация уже выполняется.");
                var archivePath = Assert.Single(Directory.GetFiles(directory.FullName, "history_*.zip"));
                Assert.Equal(archivePath, service.CurrentStatus.LastSuccessfulArchivePath);
                Assert.Equal(1, (await HistoryZipArchive.VerifyAsync(archivePath)).EventCount);
                Assert.Contains(Path.GetFileName(archivePath), settings.Message);
            }
            finally { await service.StopAsync(); }
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task Saving_history_preserves_work_mode_other_sections_and_future_fields()
    {
        var directory = Directory.CreateTempSubdirectory("promflow-history-settings-");
        try
        {
            var configPath = Path.Combine(directory.FullName, "appsettings.json");
            const string initial = """
                {
                  "Application": { "WorkMode": "user", "Other": true },
                  "Modbus": { "DataMap": [ { "Name": "keep" } ] },
                  "UnknownSection": { "Array": [1, true, "данные"] },
                  "historyarchive": { "Enabled": true, "Future": { "Key": 42 } }
                }
                """;
            await File.WriteAllTextAsync(configPath, initial);
            var config = new AppConfigService(new ConfigurationBuilder().Build(), configPath, Path.Combine(directory.FullName, "user_settings.json"));
            var expected = new HistoryArchiveOptions { Enabled = false, ArchiveIntervalMinutes = 60, Directory = directory.FullName };

            await config.SaveSectionAsync(HistoryArchiveOptions.SectionName, expected);

            using var before = JsonDocument.Parse(initial);
            using var after = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
            foreach (var key in new[] { "Application", "Modbus", "UnknownSection" })
                Assert.True(JsonElement.DeepEquals(before.RootElement.GetProperty(key), after.RootElement.GetProperty(key)));
            var history = after.RootElement.GetProperty("historyarchive");
            Assert.Equal(42, history.GetProperty("Future").GetProperty("Key").GetInt32());
            Assert.Equal(expected, history.Deserialize<HistoryArchiveOptions>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
