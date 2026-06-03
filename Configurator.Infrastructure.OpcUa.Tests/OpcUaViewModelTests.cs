using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Encoding;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Configurator.Infrastructure.OpcUa.Tests;

public sealed class OpcUaViewModelTests
{
    private static bool s_desktopResolverRegistered;
    private static bool s_reactiveUiInitialized;

    [Fact]
    public async Task AddServerTelemetry_ShouldAddRowsAndSaveOptions_WhenRuntimeStopped()
    {
        var dialog = new FakeDialogService
        {
            EditorResult = Tag("ExtraTelemetry", "DemoDevice/Telemetry/ExtraTelemetry", OpcUaTagAccess.Read)
        };
        var config = new FakeAppConfigService();
        var vm = CreateViewModel(dialog, config, OpcUaStatus.Stopped);

        await InvokeAsync(vm, "AddServerTelemetryAsync");

        Assert.Contains(Rows(vm, "ServerTelemetryRows"), row => RowName(row) == "ExtraTelemetry");
        Assert.DoesNotContain(Rows(vm, "ClientTelemetryRows"), row => RowName(row) == "ExtraTelemetry");
        Assert.NotNull(config.Settings.OpcUa);
        Assert.Contains(config.Settings.OpcUa.Nodes.ServerTelemetryTags, tag => tag.Name == "ExtraTelemetry");
        Assert.DoesNotContain(config.Settings.OpcUa.Nodes.ClientTelemetryTags, tag => tag.Name == "ExtraTelemetry");
        Assert.Equal(0, dialog.ConfirmCount);
    }

    [Fact]
    public async Task AddClientCommand_ShouldNotMutate_WhenRestartConfirmationIsCanceled()
    {
        var dialog = new FakeDialogService
        {
            ConfirmResult = false,
            EditorResult = Tag("ExtraCommand", "DemoDevice/Commands/ExtraCommand", OpcUaTagAccess.ReadWrite)
        };
        var runtime = new FakeRuntimeService
        {
            MutableStatus = new OpcUaStatus
            {
                ClientState = OpcUaConnectionStatus.Connected,
                ServerState = OpcUaConnectionStatus.Connected,
                ClientMessage = "Connected",
                ServerMessage = "Connected",
                UpdatedAt = DateTimeOffset.Now
            }
        };
        var vm = CreateViewModel(dialog, new FakeAppConfigService(), runtime);

        await InvokeAsync(vm, "AddClientCommandTagAsync");

        Assert.DoesNotContain(Rows(vm, "ClientCommandRows"), row => RowName(row) == "ExtraCommand");
        Assert.Equal(1, dialog.ConfirmCount);
        Assert.Equal(0, runtime.RestartClientCount);
        Assert.Equal(0, runtime.RestartServerCount);
    }

    [Fact]
    public async Task ImportClientCommands_ShouldAddWritableTags()
    {
        var dialog = new FakeDialogService
        {
            ImportResult = ImportResult(Tag("ImportedCommand", "Device/Commands/ImportedCommand", OpcUaTagAccess.ReadWrite))
        };
        var config = new FakeAppConfigService();
        var vm = CreateViewModel(dialog, config, OpcUaStatus.Stopped);

        await InvokeAsync(vm, "ImportClientCommandTagsAsync");

        Assert.Contains(Rows(vm, "ClientCommandRows"), row => RowName(row) == "ImportedCommand");
        Assert.DoesNotContain(Rows(vm, "ServerCommandRows"), row => RowName(row) == "ImportedCommand");
        Assert.Contains(config.Settings.OpcUa!.Nodes.ClientCommandTags, tag => tag.Name == "ImportedCommand");
        Assert.DoesNotContain(config.Settings.OpcUa!.Nodes.ServerCommandTags, tag => tag.Name == "ImportedCommand");
    }

    [Fact]
    public void OpcUaTagRow_ShouldExposeErrorState()
    {
        var rowType = LoadDesktopAssembly()
            .GetType("Configurator.Desktop.Workspace.OpcUa.OpcUaTagRow", throwOnError: true)!;
        var row = Activator.CreateInstance(
            rowType,
            Tag("Broken", "Device/Broken", OpcUaTagAccess.Read),
            false,
            null)!;

        rowType.GetMethod("SetError")!.Invoke(row, ["Read failed"]);

        Assert.True((bool)rowType.GetProperty("HasError")!.GetValue(row)!);
        Assert.Equal("Read failed", rowType.GetProperty("ErrorText")!.GetValue(row));

        rowType.GetMethod("ClearError")!.Invoke(row, null);

        Assert.False((bool)rowType.GetProperty("HasError")!.GetValue(row)!);
        Assert.Equal(string.Empty, rowType.GetProperty("ErrorText")!.GetValue(row));
    }

    [Fact]
    public void ImportDialog_AddEndpoint_ShouldAddAndSelectEndpoint()
    {
        var vm = CreateImportDialogViewModel(new FakeBrowserService());

        SetProperty(vm, "EndpointUrl", "opc.tcp://localhost:4841");
        Invoke(vm, "AddEndpoint");

        Assert.Contains(EndpointDisplays(vm), display => display.Contains("opc.tcp://localhost:4841", StringComparison.Ordinal));
        Assert.Equal("opc.tcp://localhost:4841", SelectedEndpointUrl(vm));
    }

    [Fact]
    public async Task ImportDialog_Refresh_ShouldKeepSelectionAndShowPartialWarning()
    {
        var browser = new FakeBrowserService
        {
            DiscoveryResult = new OpcUaEndpointDiscoveryResult
            {
                Endpoints =
                [
                    new()
                    {
                        Name = "PLC",
                        EndpointUrl = "opc.tcp://localhost:4842",
                        Source = "Discovery"
                    }
                ],
                Status = "Endpoints found: 2. Discovery timeout for opc.tcp://localhost:4840.",
                Warnings = ["Discovery timeout for opc.tcp://localhost:4840."]
            }
        };
        var vm = CreateImportDialogViewModel(browser);
        SetProperty(vm, "EndpointUrl", "opc.tcp://localhost:4841");
        Invoke(vm, "AddEndpoint");

        await InvokeAsync(vm, "DiscoverAsync");

        Assert.Equal("opc.tcp://localhost:4841", SelectedEndpointUrl(vm));
        Assert.Contains(EndpointDisplays(vm), display => display.Contains("opc.tcp://localhost:4842", StringComparison.Ordinal));
        Assert.Contains("Discovery timeout", (string)vm.GetType().GetProperty("StatusText")!.GetValue(vm)!);
    }

    [Fact]
    public async Task ImportDialog_Connect_ShouldBrowseSelectedEndpoint()
    {
        var browser = new FakeBrowserService();
        var vm = CreateImportDialogViewModel(browser);
        SetProperty(vm, "EndpointUrl", "opc.tcp://localhost:4842");
        Invoke(vm, "AddEndpoint");
        SetProperty(vm, "EndpointUrl", "opc.tcp://localhost:9999");

        await InvokeAsync(vm, "ConnectAsync");

        Assert.Equal("opc.tcp://localhost:4842", browser.BrowseEndpointUrl);
    }

    private static object CreateViewModel(
        FakeDialogService dialog,
        FakeAppConfigService config,
        OpcUaStatus status)
        => CreateViewModel(dialog, config, new FakeRuntimeService { MutableStatus = status });

    private static object CreateViewModel(
        FakeDialogService dialog,
        FakeAppConfigService config,
        FakeRuntimeService runtime)
    {
        var viewModelType = LoadDesktopAssembly()
            .GetType("Configurator.Desktop.Workspace.OpcUa.OpcUaViewModel", throwOnError: true)!;

        return Activator.CreateInstance(
            viewModelType,
            runtime,
            new TestOptionsMonitor(new OpcUaOptions()),
            config,
            dialog,
            new OpcUaTagConfigurationValidator())!;
    }

    private static object CreateImportDialogViewModel(FakeBrowserService browser)
    {
        var viewModelType = LoadDesktopAssembly()
            .GetType("Configurator.Desktop.Dialogs.OpcUaTagImportDialog.OpcUaTagImportDialogViewModel", throwOnError: true)!;

        return Activator.CreateInstance(
            viewModelType,
            browser,
            new OpcUaBrowseRequest
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                Options = new OpcUaOptions
                {
                    Client = { EndpointUrl = "opc.tcp://localhost:4840" },
                    Server = { EndpointUrl = "opc.tcp://localhost:4840" }
                }
            })!;
    }

    private static OpcUaConfiguredTag Tag(string name, string identifier, OpcUaTagAccess access)
        => new()
        {
            Name = name,
            Address = new OpcUaTagAddress("urn:Configurator:OpcUa:Demo", identifier, "String"),
            Access = access
        };

    private static OpcUaTagImportResult ImportResult(params OpcUaConfiguredTag[] tags)
        => new()
        {
            Tags = tags,
            SelectedEndpoint = new OpcUaEndpointProfile
            {
                Name = "Remote",
                EndpointUrl = "opc.tcp://localhost:4841",
                Source = "Test"
            },
            KnownEndpoints =
            [
                new()
                {
                    Name = "Remote",
                    EndpointUrl = "opc.tcp://localhost:4841",
                    Source = "Test"
                }
            ]
        };

    private static async Task InvokeAsync(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        var task = (Task)method.Invoke(target, null)!;
        await task;
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(target.GetType().FullName, methodName);
        method.Invoke(target, null);
    }

    private static void SetProperty(object target, string propertyName, object? value)
        => target.GetType().GetProperty(propertyName)!.SetValue(target, value);

    private static IEnumerable<object> Rows(object viewModel, string propertyName)
    {
        var value = viewModel.GetType().GetProperty(propertyName)!.GetValue(viewModel);
        return ((IEnumerable)value!).Cast<object>();
    }

    private static string RowName(object row)
        => (string)row.GetType().GetProperty("Name")!.GetValue(row)!;

    private static IEnumerable<string> EndpointDisplays(object viewModel)
    {
        var endpoints = ((IEnumerable)viewModel.GetType().GetProperty("EndpointOptions")!.GetValue(viewModel)!).Cast<object>();
        return endpoints.Select(endpoint => (string)endpoint.GetType().GetProperty("Display")!.GetValue(endpoint)!);
    }

    private static string SelectedEndpointUrl(object viewModel)
    {
        var selected = viewModel.GetType().GetProperty("SelectedEndpoint")!.GetValue(viewModel)!;
        var profile = selected.GetType().GetProperty("Profile")!.GetValue(selected)!;
        return (string)profile.GetType().GetProperty("EndpointUrl")!.GetValue(profile)!;
    }

    private static Assembly LoadDesktopAssembly()
    {
        var root = FindRepositoryRoot();
        var desktopOutput = Path.Combine(
            root.FullName,
            "Configurator.Desktop",
            "bin",
            "Debug",
            "net10.0");
        RegisterDesktopResolver(desktopOutput);
        EnsureReactiveUiInitialized();

        var path = Path.Combine(desktopOutput, "Configurator.Desktop.dll");
        return Assembly.LoadFrom(path);
    }

    private static void EnsureReactiveUiInitialized()
    {
        if (s_reactiveUiInitialized)
        {
            return;
        }

        var reactiveAssembly = AssemblyLoadContext.Default.Assemblies
            .FirstOrDefault(assembly => assembly.GetName().Name == "ReactiveUI")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(FindPackageAssembly("ReactiveUI")!);
        var builderType = reactiveAssembly.GetType("ReactiveUI.Builder.RxAppBuilder")
                          ?? reactiveAssembly.GetType("ReactiveUI.RxAppBuilder")
                          ?? throw new InvalidOperationException("ReactiveUI builder type was not found.");
        var builder = builderType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "CreateReactiveUIBuilder" && method.GetParameters().Length == 0)
            .Invoke(null, null)!;

        builder = builder.GetType().GetMethod("WithCoreServices", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(builder, null)!;
        builder.GetType().GetMethod("BuildApp", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(builder, null);
        s_reactiveUiInitialized = true;
    }

    private static void RegisterDesktopResolver(string desktopOutput)
    {
        if (s_desktopResolverRegistered)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (_, assemblyName) =>
        {
            var candidate = Path.Combine(desktopOutput, $"{assemblyName.Name}.dll");
            if (File.Exists(candidate))
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate);
            }

            var packageCandidate = FindPackageAssembly(assemblyName.Name);
            return packageCandidate is not null
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(packageCandidate)
                : null;
        };
        s_desktopResolverRegistered = true;
    }

    private static string? FindPackageAssembly(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        var packageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            assemblyName.ToLowerInvariant());
        if (!Directory.Exists(packageRoot))
        {
            return null;
        }

        foreach (var versionDirectory in Directory.GetDirectories(packageRoot).OrderByDescending(ParseVersion))
        {
            var candidates = Directory.EnumerateFiles(versionDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var candidate = candidates
                .OrderByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}net9.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => path.Contains($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static Version ParseVersion(string path)
    {
        var name = Path.GetFileName(path);
        return Version.TryParse(name, out var version) ? version : new Version(0, 0);
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

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; init; } = true;
        public int ConfirmCount { get; private set; }
        public OpcUaConfiguredTag? EditorResult { get; init; }
        public OpcUaTagImportResult? ImportResult { get; init; }

        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }

        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ModbusOptions?> EditModbusSettingsAsync(
            string title,
            string sectionName,
            ModbusOptions options,
            CancellationToken ct = default)
            => Task.FromResult<ModbusOptions?>(null);

        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(
            string title,
            OpcUaConfiguredTag? tag,
            OpcUaImportTarget target,
            CancellationToken ct = default)
            => Task.FromResult(EditorResult);

        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(
            OpcUaBrowseRequest request,
            CancellationToken ct = default)
            => Task.FromResult(ImportResult);
    }

    private sealed class FakeBrowserService : IOpcUaTagBrowserService
    {
        public string? BrowseEndpointUrl { get; private set; }

        public OpcUaEndpointDiscoveryResult DiscoveryResult { get; init; } = new()
        {
            Endpoints =
            [
                new()
                {
                    Name = "Default",
                    EndpointUrl = "opc.tcp://localhost:4840",
                    Source = "Test"
                }
            ],
            Status = "Endpoints found: 1."
        };

        public Task<OpcUaOperationResult<OpcUaBrowseResult>> BrowseAsync(
            OpcUaBrowseRequest request,
            CancellationToken cancellationToken = default)
        {
            BrowseEndpointUrl = request.EndpointUrl;
            return Task.FromResult(OpcUaOperationResult<OpcUaBrowseResult>.Success(new OpcUaBrowseResult
            {
                Nodes = [],
                Status = "Connected. Nodes read: 0."
            }));
        }

        public Task<OpcUaOperationResult<OpcUaEndpointDiscoveryResult>> DiscoverEndpointsAsync(
            OpcUaEndpointDiscoveryRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(OpcUaOperationResult<OpcUaEndpointDiscoveryResult>.Success(DiscoveryResult));
    }

    private sealed class FakeAppConfigService : IAppConfigService
    {
        public UserSettings Settings { get; private set; } = new();

        public T GetSection<T>(string sectionName) where T : class, new()
            => new();

        public string GetValue(string key)
            => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
            => Task.CompletedTask;

        public void SaveUserSettings(UserSettings settings)
        {
            Settings = settings;
        }

        public UserSettings LoadUserSettings()
            => Settings;
    }

    private sealed class FakeRuntimeService : IOpcUaRuntimeService
    {
        public OpcUaStatus MutableStatus { get; init; } = OpcUaStatus.Stopped;
        public int RestartClientCount { get; private set; }
        public int RestartServerCount { get; private set; }

        public OpcUaStatus Status => MutableStatus;
        public OpcUaSnapshot ClientSnapshot => OpcUaSnapshot.Empty;
        public OpcUaSnapshot ServerSnapshot => OpcUaSnapshot.Empty;
        public OpcUaOptions CurrentOptions { get; private set; } = new();

        public event EventHandler<OpcUaStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event EventHandler<OpcUaSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task StartAsync(OpcUaRunMode mode = OpcUaRunMode.Both, OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartAsync(OpcUaRunMode mode = OpcUaRunMode.Both, OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopClientAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartClientAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            RestartClientCount++;
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task StopServerAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RestartServerAsync(OpcUaOptions? options = null, CancellationToken cancellationToken = default)
        {
            RestartServerCount++;
            CurrentOptions = options ?? CurrentOptions;
            return Task.CompletedTask;
        }

        public Task<OpcUaOperationResult> WriteClientTagAsync(OpcUaTagWriteRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(OpcUaOperationResult.Success());

        public Task<OpcUaOperationResult> UpdateServerTagAsync(OpcUaTagValue value, CancellationToken cancellationToken = default)
            => Task.FromResult(OpcUaOperationResult.Success());

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<OpcUaOptions>
    {
        public TestOptionsMonitor(OpcUaOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public OpcUaOptions CurrentValue { get; }

        public OpcUaOptions Get(string? name)
            => CurrentValue;

        public IDisposable? OnChange(Action<OpcUaOptions, string?> listener)
            => null;
    }

}
