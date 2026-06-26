using Xunit;

namespace Configurator.Tests.Unit.Hardening;

public sealed class ProductionDocumentationTests
{
    private static readonly string[] GuideFiles =
    [
        Path.Combine("AgentDocs", "en", "07-archive-guide.en.md"),
        Path.Combine("AgentDocs", "en", "08-authorization-guide.en.md"),
        Path.Combine("AgentDocs", "en", "09-offline-license-guide.en.md"),
        Path.Combine("AgentDocs", "en", "10-operations-and-recovery.en.md"),
        Path.Combine("AgentDocs", "en", "11-operator-user-guide.en.md"),
        Path.Combine("AgentDocs", "en", "12-license-issuer-guide.en.md"),
        Path.Combine("AgentDocs", "en", "13-database-guide.en.md"),
        Path.Combine("AgentDocs", "ru", "07-archive-guide.ru.md"),
        Path.Combine("AgentDocs", "ru", "08-authorization-guide.ru.md"),
        Path.Combine("AgentDocs", "ru", "09-offline-license-guide.ru.md"),
        Path.Combine("AgentDocs", "ru", "10-operations-and-recovery.ru.md"),
        Path.Combine("AgentDocs", "ru", "11-operator-user-guide.ru.md"),
        Path.Combine("AgentDocs", "ru", "12-license-issuer-guide.ru.md"),
        Path.Combine("AgentDocs", "ru", "13-database-guide.ru.md")
    ];

    [Fact]
    public void Guides_ExistInEnglishAndRussianAndMasterLinksThem()
    {
        var root = FindRepositoryRoot();

        foreach (var guideFile in GuideFiles)
        {
            var path = Path.Combine(root, guideFile);
            Assert.True(File.Exists(path), $"Missing guide: {guideFile}");
            Assert.True(new FileInfo(path).Length > 500, $"Guide is unexpectedly short: {guideFile}");
        }

        var englishMaster = File.ReadAllText(Path.Combine(root, "AgentDocs", "en", "00-master.en.md"));
        var russianMaster = File.ReadAllText(Path.Combine(root, "AgentDocs", "ru", "00-master.ru.md"));

        Assert.Contains("07-archive-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("08-authorization-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("09-offline-license-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("10-operations-and-recovery.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("11-operator-user-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("12-license-issuer-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("13-database-guide.en.md", englishMaster, StringComparison.Ordinal);
        Assert.Contains("07-archive-guide.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("08-authorization-guide.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("09-offline-license-guide.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("10-operations-and-recovery.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("11-operator-user-guide.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("12-license-issuer-guide.ru.md", russianMaster, StringComparison.Ordinal);
        Assert.Contains("13-database-guide.ru.md", russianMaster, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationDocs_DoNotReferenceMissingRussianProgressFiles()
    {
        var root = FindRepositoryRoot();
        var files = Directory
            .EnumerateFiles(Path.Combine(root, "AgentDocs"), "*.md", SearchOption.AllDirectories)
            .Concat([Path.Combine(root, "AGENTS.md")])
            .ToArray();

        var failures = files
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path),
                Text = File.ReadAllText(path)
            })
            .Where(file =>
                file.Text.Contains("00-implementation-master.ru.md", StringComparison.Ordinal)
                || file.Text.Contains("PROGRESS.ru.md", StringComparison.Ordinal))
            .Select(file => file.Path)
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void LicenseIssuerGuide_DocumentsCommandsProfilesAndFeatureRules()
    {
        var root = FindRepositoryRoot();
        var english = File.ReadAllText(Path.Combine(root, "AgentDocs", "en", "12-license-issuer-guide.en.md"));
        var russian = File.ReadAllText(Path.Combine(root, "AgentDocs", "ru", "12-license-issuer-guide.ru.md"));

        AssertGuideContainsAll(
            english,
            [
                "dotnet run --project .\\Configurator.LicenseIssuer\\Configurator.LicenseIssuer.csproj -- --help",
                "generate-key --key-id",
                "issue --profile",
                "verify --license",
                "inspect --license",
                "--test-key",
                "--allow-test-key",
                "Exit Codes",
                "customer-profile.json",
                "\"maximumExclusive\"",
                "\"bindingMode\": \"InstallationId\"",
                "\"bindingMode\": \"None\"",
                "Community",
                "Professional",
                "RouteMap",
                "RemoteControl",
                "ArchiveExport",
                "EngineeringTools",
                "Diagnostics",
                "full-package-1-day",
                "$durationDays = 30",
                "full-package-1-year",
                "TrustedPublicKeys"
            ]);

        AssertGuideContainsAll(
            russian,
            [
                "Configurator.LicenseIssuer",
                "generate-key --key-id",
                "issue --profile",
                "verify --license",
                "inspect --license",
                "--allow-test-key",
                "customer-profile.json",
                "\"maximumExclusive\"",
                "\"bindingMode\": \"InstallationId\"",
                "\"bindingMode\": \"None\"",
                "Community",
                "Professional",
                "RouteMap",
                "RemoteControl",
                "ArchiveExport",
                "EngineeringTools",
                "Diagnostics",
                "full-package-1-day",
                "$durationDays = 30",
                "full-package-1-year",
                "TrustedPublicKeys"
            ]);
    }

    [Fact]
    public void DatabaseGuide_DocumentsSqlitePathsSnapshotQueriesAndBlobFormat()
    {
        var root = FindRepositoryRoot();
        var english = File.ReadAllText(Path.Combine(root, "AgentDocs", "en", "13-database-guide.en.md"));
        var russian = File.ReadAllText(Path.Combine(root, "AgentDocs", "ru", "13-database-guide.ru.md"));

        AssertGuideContainsAll(
            english,
            [
                "sqlite3 -readonly",
                "DB Browser for SQLite",
                "promflow-security.sqlite",
                "Authentication.SecurityDatabasePath",
                "Archive.BaseDirectory",
                "promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite",
                ".sqlite-wal",
                ".sqlite-shm",
                "PRAGMA integrity_check;",
                "BEGIN IMMEDIATE;",
                "app_user",
                "archive_partition_metadata",
                "modbus_snapshot",
                "runtime_event",
                "equipment_command",
                "modbus_write",
                "security_audit",
                "hex(coils_blob)",
                "hex(holding_registers_blob)",
                "PFS1",
                "12-byte header",
                "Item type: `1` coils, `2` holding registers",
                "coil_start_address + index",
                "holding_register_start_address + n",
                "snapshots.ndjson"
            ]);

        AssertGuideContainsAll(
            russian,
            [
                "sqlite3 -readonly",
                "DB Browser for SQLite",
                "promflow-security.sqlite",
                "Authentication.SecurityDatabasePath",
                "Archive.BaseDirectory",
                "promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite",
                ".sqlite-wal",
                ".sqlite-shm",
                "PRAGMA integrity_check;",
                "BEGIN IMMEDIATE;",
                "app_user",
                "archive_partition_metadata",
                "modbus_snapshot",
                "runtime_event",
                "equipment_command",
                "modbus_write",
                "security_audit",
                "hex(coils_blob)",
                "hex(holding_registers_blob)",
                "PFS1",
                "12-byte header",
                "Item type: `1` coils, `2` holding registers",
                "coil_start_address + index",
                "holding_register_start_address + n",
                "snapshots.ndjson"
            ]);
    }

    [Fact]
    public void ArchitectureAndTestingDocs_DescribeCurrentLifecycleAndStage14Acceptance()
    {
        var root = FindRepositoryRoot();
        var architectureDocs = new[]
        {
            File.ReadAllText(Path.Combine(root, "AgentDocs", "en", "01-architecture-overview.en.md")),
            File.ReadAllText(Path.Combine(root, "AgentDocs", "ru", "01-architecture-overview.ru.md"))
        };

        foreach (var architectureDoc in architectureDocs)
        {
            Assert.DoesNotContain("The current `WorkspaceView` has three tabs", architectureDoc, StringComparison.Ordinal);
            Assert.DoesNotContain("It may autostart the shared", architectureDoc, StringComparison.Ordinal);
            Assert.Contains("ApplicationRuntimeCoordinator", architectureDoc, StringComparison.Ordinal);
            Assert.Contains("dynamic workspace", architectureDoc, StringComparison.OrdinalIgnoreCase);
        }

        var englishTesting = File.ReadAllText(Path.Combine(root, "AgentDocs", "en", "06-testing-and-diagnostics.en.md"));
        var russianTesting = File.ReadAllText(Path.Combine(root, "AgentDocs", "ru", "06-testing-and-diagnostics.ru.md"));

        Assert.Contains("Stage 14", englishTesting, StringComparison.Ordinal);
        Assert.Contains("STAGE14-ACCEPTANCE.md", englishTesting, StringComparison.Ordinal);
        Assert.Contains("Stage 14", russianTesting, StringComparison.Ordinal);
        Assert.Contains("STAGE14-ACCEPTANCE.md", russianTesting, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptanceReport_ListsProductionEvidenceAndKeepsStage15OutOfScope()
    {
        var root = FindRepositoryRoot();
        var report = File.ReadAllText(Path.Combine(root, "AgentDocs", "implementation", "STAGE14-ACCEPTANCE.md"));

        var expectedScenarios = new[]
        {
            "disabled current user",
            "disabled different user",
            "license expiry during session",
            "archive load matrix",
            "archive queue pressure",
            "failed archive transaction",
            "corrupted archive partition",
            "large export limit",
            "connection loss during command",
            "pulse shutdown interruption",
            "source scan",
            "documentation coverage",
            "operations runbook",
            "full verification"
        };

        foreach (var scenario in expectedScenarios)
        {
            Assert.Contains(scenario, report, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("Stage 15: not started", report, StringComparison.Ordinal);
    }

    private static void AssertGuideContainsAll(string text, IReadOnlyList<string> requiredFragments)
    {
        foreach (var fragment in requiredFragments)
        {
            Assert.Contains(fragment, text, StringComparison.Ordinal);
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
