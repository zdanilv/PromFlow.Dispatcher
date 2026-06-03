using Avalonia.Threading;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
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
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.OpcUa;

/// <summary>
/// Модель представления OPC UA экрана: управляет настройками клиента/сервера, тегами, командами runtime и журналом UI.
/// </summary>
public sealed class OpcUaViewModel : ViewModelBase, IDisposable
{
    private readonly IAppConfigService _appConfigService;
    private readonly IDialogService _dialogService;
    private readonly Dictionary<string, DateTimeOffset> _eventTimes = new();
    private readonly IOpcUaRuntimeService _runtimeService;
    private readonly IOpcUaTagConfigurationValidator _tagConfigurationValidator;
    private readonly List<OpcUaEndpointProfile> _knownEndpoints = [];
    private bool _certificatesEnabled;
    private bool _clientEnabled = true;
    private string _clientEndpointUrl = "opc.tcp://localhost:4840";
    private string _clientMessage = "Client stopped";
    private string _clientState = OpcUaConnectionStatus.Disconnected.ToString();
    private bool _isBothCommandRunning;
    private bool _isClientCommandRunning;
    private bool _isServerCommandRunning;
    private string _lastError = string.Empty;
    private string _lastUpdated = "-";
    private string _namespaceUri = "urn:Configurator:OpcUa:Demo";
    private bool _serverDemoTelemetryEnabled = true;
    private bool _serverEnabled = true;
    private string _serverEndpointUrl = "opc.tcp://localhost:4840";
    private string _serverMessage = "Server stopped";
    private string _serverState = OpcUaConnectionStatus.Disconnected.ToString();
    private int _serverTelemetryUpdateIntervalMs = 1000;
    private OpcUaTagRow? _selectedClientCommandRow;
    private OpcUaTagRow? _selectedClientTelemetryRow;
    private OpcUaTagRow? _selectedServerTelemetryRow;

    /// <summary>
    /// Создает экран OPC UA, загружает сохраненные настройки и подписывается на runtime.
    /// </summary>
    public OpcUaViewModel(
        IOpcUaRuntimeService runtimeService,
        IOptionsMonitor<OpcUaOptions> optionsMonitor,
        IAppConfigService appConfigService,
        IDialogService dialogService,
        IOpcUaTagConfigurationValidator tagConfigurationValidator)
    {
        _runtimeService = runtimeService;
        _appConfigService = appConfigService;
        _dialogService = dialogService;
        _tagConfigurationValidator = tagConfigurationValidator;

        var persistedOptions = _appConfigService.LoadUserSettings().OpcUa;
        ApplyOptions(persistedOptions ?? optionsMonitor.CurrentValue);
        ApplyStatus(_runtimeService.Status);
        ApplySnapshot(_runtimeService.ClientSnapshot);
        ApplySnapshot(_runtimeService.ServerSnapshot);

        _runtimeService.StatusChanged += OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged += OnRuntimeSnapshotChanged;

        StartBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Start client and server", StartBothAsync), this.WhenAnyValue(x => x.CanStartBoth));
        StopBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Stop client and server", () => _runtimeService.StopAsync()), this.WhenAnyValue(x => x.CanStopBoth));
        RestartBothCommand = ReactiveCommand.CreateFromTask(() => RunBothCommandAsync("Restart client and server", RestartBothAsync), this.WhenAnyValue(x => x.CanRestartBoth));
        StartClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Start client", StartClientAsync), this.WhenAnyValue(x => x.CanStartClient));
        StopClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Stop client", () => _runtimeService.StopClientAsync()), this.WhenAnyValue(x => x.CanStopClient));
        RestartClientCommand = ReactiveCommand.CreateFromTask(() => RunClientCommandAsync("Restart client", RestartClientAsync), this.WhenAnyValue(x => x.CanRestartClient));
        StartServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Start server", StartServerAsync), this.WhenAnyValue(x => x.CanStartServer));
        StopServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Stop server", () => _runtimeService.StopServerAsync()), this.WhenAnyValue(x => x.CanStopServer));
        RestartServerCommand = ReactiveCommand.CreateFromTask(() => RunServerCommandAsync("Restart server", RestartServerAsync), this.WhenAnyValue(x => x.CanRestartServer));

        AddServerTelemetryCommand = ReactiveCommand.CreateFromTask(AddServerTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        RemoveServerTelemetryCommand = ReactiveCommand.CreateFromTask(RemoveServerTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        ImportServerTelemetryCommand = ReactiveCommand.CreateFromTask(ImportServerTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        AddClientTelemetryCommand = ReactiveCommand.CreateFromTask(AddClientTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        RemoveClientTelemetryCommand = ReactiveCommand.CreateFromTask(RemoveClientTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        ImportClientTelemetryCommand = ReactiveCommand.CreateFromTask(ImportClientTelemetryAsync, this.WhenAnyValue(x => x.CanModifyTags));
        AddClientCommandTagCommand = ReactiveCommand.CreateFromTask(AddClientCommandTagAsync, this.WhenAnyValue(x => x.CanModifyTags));
        RemoveClientCommandTagCommand = ReactiveCommand.CreateFromTask(RemoveClientCommandTagAsync, this.WhenAnyValue(x => x.CanModifyTags));
        ImportClientCommandTagsCommand = ReactiveCommand.CreateFromTask(ImportClientCommandTagsAsync, this.WhenAnyValue(x => x.CanModifyTags));

        RaiseCommandStateChanged();
    }

    /// <summary>
    /// Теги телеметрии, которые desktop-клиент читает с внешнего OPC UA сервера.
    /// </summary>
    public ObservableCollection<OpcUaTagRow> ClientTelemetryRows { get; } = new();

    /// <summary>
    /// Командные теги, которые desktop-клиент записывает во внешний OPC UA сервер.
    /// </summary>
    public ObservableCollection<OpcUaTagRow> ClientCommandRows { get; } = new();

    /// <summary>
    /// Теги телеметрии, публикуемые встроенным OPC UA сервером.
    /// </summary>
    public ObservableCollection<OpcUaTagRow> ServerTelemetryRows { get; } = new();

    /// <summary>
    /// Командные теги встроенного сервера, доступные внешним клиентам для записи.
    /// </summary>
    public ObservableCollection<OpcUaTagRow> ServerCommandRows { get; } = new();

    /// <summary>
    /// Короткий журнал операций OPC UA экрана.
    /// </summary>
    public ObservableCollection<OpcUaEventRow> Events { get; } = new();

    public ReactiveCommand<Unit, Unit> StartBothCommand { get; }
    public ReactiveCommand<Unit, Unit> StopBothCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartBothCommand { get; }
    public ReactiveCommand<Unit, Unit> StartClientCommand { get; }
    public ReactiveCommand<Unit, Unit> StopClientCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartClientCommand { get; }
    public ReactiveCommand<Unit, Unit> StartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> StopServerCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartServerCommand { get; }
    public ReactiveCommand<Unit, Unit> AddServerTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveServerTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportServerTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddClientTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveClientTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportClientTelemetryCommand { get; }
    public ReactiveCommand<Unit, Unit> AddClientCommandTagCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveClientCommandTagCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportClientCommandTagsCommand { get; }

    public bool CanStartClient => !IsClientCommandRunning && !IsBothCommandRunning && IsStartable(ParseState(ClientState));
    public bool CanStopClient => !IsClientCommandRunning && !IsBothCommandRunning && IsStoppable(ParseState(ClientState));
    public bool CanRestartClient => !IsClientCommandRunning && !IsBothCommandRunning && ParseState(ClientState) != OpcUaConnectionStatus.Disconnected;
    public bool CanStartServer => !IsServerCommandRunning && !IsBothCommandRunning && IsStartable(ParseState(ServerState));
    public bool CanStopServer => !IsServerCommandRunning && !IsBothCommandRunning && IsStoppable(ParseState(ServerState));
    public bool CanRestartServer => !IsServerCommandRunning && !IsBothCommandRunning && ParseState(ServerState) != OpcUaConnectionStatus.Disconnected;
    public bool CanStartBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (CanStartClient || CanStartServer);
    public bool CanStopBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (CanStopClient || CanStopServer);
    public bool CanRestartBoth => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning && (CanRestartClient || CanRestartServer);
    public bool CanModifyTags => !IsBothCommandRunning && !IsClientCommandRunning && !IsServerCommandRunning;
    /// <summary>
    /// Можно ли записывать командные теги через клиентскую роль.
    /// </summary>
    public bool CanWriteClientCommands => ParseState(ClientState) == OpcUaConnectionStatus.Connected && CanModifyTags;

    /// <summary>
    /// Можно ли обновлять телеметрию встроенного сервера из UI.
    /// </summary>
    public bool CanWriteServerTelemetry => ParseState(ServerState) == OpcUaConnectionStatus.Connected && CanModifyTags;

    public OpcUaTagRow? SelectedServerTelemetryRow
    {
        get => _selectedServerTelemetryRow;
        set => this.RaiseAndSetIfChanged(ref _selectedServerTelemetryRow, value);
    }

    public OpcUaTagRow? SelectedClientTelemetryRow
    {
        get => _selectedClientTelemetryRow;
        set => this.RaiseAndSetIfChanged(ref _selectedClientTelemetryRow, value);
    }

    public OpcUaTagRow? SelectedClientCommandRow
    {
        get => _selectedClientCommandRow;
        set => this.RaiseAndSetIfChanged(ref _selectedClientCommandRow, value);
    }

    public bool IsClientCommandRunning
    {
        get => _isClientCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isClientCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool IsServerCommandRunning
    {
        get => _isServerCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isServerCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool IsBothCommandRunning
    {
        get => _isBothCommandRunning;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBothCommandRunning, value);
            RaiseCommandStateChanged();
        }
    }

    public bool ClientEnabled
    {
        get => _clientEnabled;
        set => this.RaiseAndSetIfChanged(ref _clientEnabled, value);
    }

    public bool ServerEnabled
    {
        get => _serverEnabled;
        set => this.RaiseAndSetIfChanged(ref _serverEnabled, value);
    }

    public bool ServerDemoTelemetryEnabled
    {
        get => _serverDemoTelemetryEnabled;
        set => this.RaiseAndSetIfChanged(ref _serverDemoTelemetryEnabled, value);
    }

    public string ClientEndpointUrl
    {
        get => _clientEndpointUrl;
        set => this.RaiseAndSetIfChanged(ref _clientEndpointUrl, value);
    }

    public string ServerEndpointUrl
    {
        get => _serverEndpointUrl;
        set => this.RaiseAndSetIfChanged(ref _serverEndpointUrl, value);
    }

    public int ServerTelemetryUpdateIntervalMs
    {
        get => _serverTelemetryUpdateIntervalMs;
        set => this.RaiseAndSetIfChanged(ref _serverTelemetryUpdateIntervalMs, value);
    }

    public string NamespaceUri
    {
        get => _namespaceUri;
        set => this.RaiseAndSetIfChanged(ref _namespaceUri, value);
    }

    public string SecurityMode => "None";

    public string AuthenticationMode => "Anonymous";

    public bool CertificatesEnabled
    {
        get => _certificatesEnabled;
        private set => this.RaiseAndSetIfChanged(ref _certificatesEnabled, value);
    }

    public string ClientState
    {
        get => _clientState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _clientState, value);
            RaiseCommandStateChanged();
        }
    }

    public string ServerState
    {
        get => _serverState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _serverState, value);
            RaiseCommandStateChanged();
        }
    }

    public string ClientMessage
    {
        get => _clientMessage;
        private set => this.RaiseAndSetIfChanged(ref _clientMessage, value);
    }

    public string ServerMessage
    {
        get => _serverMessage;
        private set => this.RaiseAndSetIfChanged(ref _serverMessage, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => this.RaiseAndSetIfChanged(ref _lastError, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
    }

    /// <summary>
    /// Отписывается от событий runtime.
    /// </summary>
    public void Dispose()
    {
        _runtimeService.StatusChanged -= OnRuntimeStatusChanged;
        _runtimeService.SnapshotChanged -= OnRuntimeSnapshotChanged;
    }

    private async Task StartBothAsync()
    {
        await _runtimeService.StartAsync(OpcUaRunMode.Both, BuildOptions());
    }

    private async Task RestartBothAsync()
    {
        await _runtimeService.RestartAsync(OpcUaRunMode.Both, BuildOptions());
    }

    private async Task StartClientAsync()
    {
        await _runtimeService.StartClientAsync(BuildOptions());
    }

    private async Task RestartClientAsync()
    {
        await _runtimeService.RestartClientAsync(BuildOptions());
    }

    private async Task StartServerAsync()
    {
        await _runtimeService.StartServerAsync(BuildOptions());
    }

    private async Task RestartServerAsync()
    {
        await _runtimeService.RestartServerAsync(BuildOptions());
    }

    private async Task AddServerTelemetryAsync()
    {
        var tag = await _dialogService.EditOpcUaTagAsync(
            "Add server telemetry tag",
            CreateManualTag(OpcUaImportTarget.ServerTelemetry),
            OpcUaImportTarget.ServerTelemetry);
        if (tag is null)
        {
            return;
        }

        var telemetry = CurrentServerTelemetryTags();
        telemetry.Add(tag);
        await ApplyTagListsAsync(
            telemetry,
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: true,
            clientChanged: false,
            "Add server telemetry tag");
    }

    private async Task RemoveServerTelemetryAsync()
    {
        if (SelectedServerTelemetryRow is null)
        {
            return;
        }

        var telemetry = CurrentServerTelemetryTags()
            .Where(tag => !SameAddress(tag.Address, SelectedServerTelemetryRow.Address))
            .ToList();
        await ApplyTagListsAsync(
            telemetry,
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: true,
            clientChanged: false,
            $"Remove server telemetry tag {SelectedServerTelemetryRow.Name}");
    }

    private async Task ImportServerTelemetryAsync()
    {
        var imported = await _dialogService.ImportOpcUaTagsAsync(new OpcUaBrowseRequest
        {
            EndpointUrl = NormalizeEndpoint(ServerEndpointUrl),
            Target = OpcUaImportTarget.ServerTelemetry,
            Options = BuildOptions()
        });

        if (imported is null)
        {
            return;
        }

        var previousEndpoints = _knownEndpoints.Select(endpoint => endpoint.Clone()).ToList();
        ApplyKnownEndpoints(imported.KnownEndpoints);
        var serverTags = imported.Tags
            .Where(tag => tag.IsReadable)
            .Select(ToLocalServerTelemetryTag);
        var telemetry = MergeTags(CurrentServerTelemetryTags(), serverTags);
        var applied = await ApplyTagListsAsync(
            telemetry,
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: true,
            clientChanged: false,
            "Import server telemetry tags");
        if (!applied)
        {
            _knownEndpoints.Clear();
            _knownEndpoints.AddRange(previousEndpoints);
        }
    }

    private async Task AddClientTelemetryAsync()
    {
        var tag = await _dialogService.EditOpcUaTagAsync(
            "Add client telemetry tag",
            CreateManualTag(OpcUaImportTarget.ServerTelemetry, clientSide: true),
            OpcUaImportTarget.ServerTelemetry);
        if (tag is null)
        {
            return;
        }

        var telemetry = CurrentClientTelemetryTags();
        telemetry.Add(tag);
        await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            telemetry,
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: false,
            clientChanged: true,
            "Add client telemetry tag");
    }

    private async Task RemoveClientTelemetryAsync()
    {
        if (SelectedClientTelemetryRow is null)
        {
            return;
        }

        var telemetry = CurrentClientTelemetryTags()
            .Where(tag => !SameAddress(tag.Address, SelectedClientTelemetryRow.Address))
            .ToList();
        await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            telemetry,
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: false,
            clientChanged: true,
            $"Remove client telemetry tag {SelectedClientTelemetryRow.Name}");
    }

    private async Task ImportClientTelemetryAsync()
    {
        var imported = await _dialogService.ImportOpcUaTagsAsync(new OpcUaBrowseRequest
        {
            EndpointUrl = NormalizeEndpoint(ClientEndpointUrl),
            Target = OpcUaImportTarget.ServerTelemetry,
            Options = BuildOptions()
        });

        if (imported is null)
        {
            return;
        }

        var previousEndpoint = ClientEndpointUrl;
        var previousEndpoints = _knownEndpoints.Select(endpoint => endpoint.Clone()).ToList();
        ApplyKnownEndpoints(imported.KnownEndpoints);
        ApplySelectedClientEndpoint(imported.SelectedEndpoint);
        var telemetry = MergeTags(CurrentClientTelemetryTags(), imported.Tags.Where(tag => tag.IsReadable));
        var applied = await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            telemetry,
            CurrentServerCommandTags(),
            CurrentClientCommandTags(),
            serverChanged: false,
            clientChanged: true,
            "Import client telemetry tags");
        if (!applied)
        {
            ClientEndpointUrl = previousEndpoint;
            _knownEndpoints.Clear();
            _knownEndpoints.AddRange(previousEndpoints);
        }
    }

    private async Task AddClientCommandTagAsync()
    {
        var tag = await _dialogService.EditOpcUaTagAsync(
            "Add client command tag",
            CreateManualTag(OpcUaImportTarget.ClientCommand),
            OpcUaImportTarget.ClientCommand);
        if (tag is null)
        {
            return;
        }

        var commands = CurrentClientCommandTags();
        commands.Add(tag);
        await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            commands,
            serverChanged: false,
            clientChanged: true,
            "Add client command tag");
    }

    private async Task RemoveClientCommandTagAsync()
    {
        if (SelectedClientCommandRow is null)
        {
            return;
        }

        var commands = CurrentClientCommandTags()
            .Where(tag => !SameAddress(tag.Address, SelectedClientCommandRow.Address))
            .ToList();
        await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            commands,
            serverChanged: false,
            clientChanged: true,
            $"Remove client command tag {SelectedClientCommandRow.Name}");
    }

    private async Task ImportClientCommandTagsAsync()
    {
        var imported = await _dialogService.ImportOpcUaTagsAsync(new OpcUaBrowseRequest
        {
            EndpointUrl = NormalizeEndpoint(ClientEndpointUrl),
            Target = OpcUaImportTarget.ClientCommand,
            Options = BuildOptions()
        });

        if (imported is null)
        {
            return;
        }

        var previousEndpoint = ClientEndpointUrl;
        var previousEndpoints = _knownEndpoints.Select(endpoint => endpoint.Clone()).ToList();
        ApplyKnownEndpoints(imported.KnownEndpoints);
        ApplySelectedClientEndpoint(imported.SelectedEndpoint);
        var commands = MergeTags(CurrentClientCommandTags(), imported.Tags.Where(tag => tag.IsWritable));
        var applied = await ApplyTagListsAsync(
            CurrentServerTelemetryTags(),
            CurrentClientTelemetryTags(),
            CurrentServerCommandTags(),
            commands,
            serverChanged: false,
            clientChanged: true,
            "Import client command tags");
        if (!applied)
        {
            ClientEndpointUrl = previousEndpoint;
            _knownEndpoints.Clear();
            _knownEndpoints.AddRange(previousEndpoints);
        }
    }

    private async Task<bool> ApplyTagListsAsync(
        IReadOnlyList<OpcUaConfiguredTag> serverTelemetryTags,
        IReadOnlyList<OpcUaConfiguredTag> clientTelemetryTags,
        IReadOnlyList<OpcUaConfiguredTag> serverCommandTags,
        IReadOnlyList<OpcUaConfiguredTag> clientCommandTags,
        bool serverChanged,
        bool clientChanged,
        string eventText)
    {
        var validation = _tagConfigurationValidator.ValidateLists(serverTelemetryTags, serverCommandTags);
        if (validation.Succeeded)
        {
            validation = _tagConfigurationValidator.ValidateLists(clientTelemetryTags, clientCommandTags);
        }

        if (!validation.Succeeded)
        {
            ReportOperationError(validation);
            return false;
        }

        var serverWasRunning = IsRoleRunning(ServerState);
        var clientWasRunning = IsRoleRunning(ClientState);
        if (!await ConfirmRuntimeChangeAsync(
                serverChanged && serverWasRunning,
                clientChanged && clientWasRunning,
                serverChanged && serverWasRunning && clientWasRunning && SameEndpoint(ClientEndpointUrl, ServerEndpointUrl)))
        {
            AddEvent("Config", "Tag change canceled");
            return false;
        }

        RebuildRowsFromDefinitions(serverTelemetryTags, clientTelemetryTags, serverCommandTags, clientCommandTags);
        SaveOpcUaOptions();
        AddEvent("Config", eventText);

        await RestartAffectedRuntimeAsync(
            serverChanged && serverWasRunning,
            clientChanged && clientWasRunning,
            clientWasRunning && serverChanged && SameEndpoint(ClientEndpointUrl, ServerEndpointUrl));

        return true;
    }

    private async Task<bool> ConfirmRuntimeChangeAsync(
        bool restartServer,
        bool reconnectClient,
        bool clientAffectedByServerRestart)
    {
        if (!restartServer && !reconnectClient && !clientAffectedByServerRestart)
        {
            return true;
        }

        var messages = new List<string>();
        if (restartServer)
        {
            messages.Add("OPC UA server is running. Changing tag lists changes the server address space, so the server must be restarted.");
        }

        if (clientAffectedByServerRestart)
        {
            messages.Add("The client is connected to the same endpoint. It will be stopped before the server restart and connected again after it.");
        }
        else if (reconnectClient)
        {
            messages.Add("OPC UA client is running. The client will reconnect to apply the new tag list and subscriptions.");
        }

        messages.Add("Apply the change now?");
        return await _dialogService.ConfirmAsync(string.Join(Environment.NewLine + Environment.NewLine, messages));
    }

    private async Task RestartAffectedRuntimeAsync(
        bool restartServer,
        bool reconnectClient,
        bool clientAffectedByServerRestart)
    {
        if (!restartServer && !reconnectClient && !clientAffectedByServerRestart)
        {
            return;
        }

        IsBothCommandRunning = true;
        try
        {
            var options = BuildOptions();
            if (restartServer && clientAffectedByServerRestart)
            {
                await _runtimeService.StopClientAsync();
                await _runtimeService.RestartServerAsync(options);
                await _runtimeService.StartClientAsync(options);
            }
            else
            {
                if (restartServer)
                {
                    await _runtimeService.RestartServerAsync(options);
                }

                if (reconnectClient)
                {
                    await _runtimeService.RestartClientAsync(options);
                }
            }
        }
        finally
        {
            IsBothCommandRunning = false;
        }
    }

    private async Task RunClientCommandAsync(string eventText, Func<Task> action)
    {
        IsClientCommandRunning = true;
        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsClientCommandRunning = false;
        }
    }

    private async Task RunServerCommandAsync(string eventText, Func<Task> action)
    {
        IsServerCommandRunning = true;
        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsServerCommandRunning = false;
        }
    }

    private async Task RunBothCommandAsync(string eventText, Func<Task> action)
    {
        IsBothCommandRunning = true;
        try
        {
            await RunAsync(eventText, action);
        }
        finally
        {
            IsBothCommandRunning = false;
        }
    }

    private async Task RunAsync(string eventText, Func<Task> action)
    {
        AddEvent("UI", eventText);

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            AddEvent("Error", LastError, "Error", ex.ToString());
        }
    }

    private async Task WriteClientCommandAsync(OpcUaTagRow row, object? value)
    {
        var result = await _runtimeService.WriteClientTagAsync(
            new OpcUaTagWriteRequest(row.Address, value));
        if (result.Succeeded)
        {
            row.ClearError();
            AddEvent("Client", $"Write {row.Name}={value}");
            return;
        }

        row.SetError(OperationMessage(result, "OPC UA client write failed."));
        ReportOperationError(result);
    }

    private async Task UpdateServerTelemetryAsync(OpcUaTagRow row, object? value)
    {
        var result = await _runtimeService.UpdateServerTagAsync(
            new OpcUaTagValue(row.Address, value, DateTimeOffset.Now, "Good"));
        if (result.Succeeded)
        {
            row.ClearError();
            AddEvent("Server", $"Update {row.Name}={value}");
            return;
        }

        row.SetError(OperationMessage(result, "OPC UA server update failed."));
        ReportOperationError(result);
    }

    private OpcUaOptions BuildOptions()
    {
        // UI хранит настройки в отдельных свойствах и коллекциях строк,
        // а runtime принимает цельный снимок настроек для каждой команды.
        return new OpcUaOptions
        {
            AutostartOnWorkspaceOpen = false,
            StartupMode = OpcUaRunMode.None,
            Client = new OpcUaClientOptions
            {
                Enabled = ClientEnabled,
                EndpointUrl = NormalizeEndpoint(ClientEndpointUrl),
                ApplicationName = "DekstopTemplate OPC UA Client",
                ApplicationUri = "urn:localhost:DekstopTemplate:OpcUa:Client",
                ProductUri = "urn:DekstopTemplate",
                SubscriptionPublishingIntervalMilliseconds = 1000
            },
            Server = new OpcUaServerOptions
            {
                Enabled = ServerEnabled,
                DemoTelemetryEnabled = ServerDemoTelemetryEnabled,
                EndpointUrl = NormalizeEndpoint(ServerEndpointUrl),
                ApplicationName = "DekstopTemplate OPC UA Server",
                ApplicationUri = "urn:localhost:DekstopTemplate:OpcUa:Server",
                ProductUri = "urn:DekstopTemplate",
                TelemetryUpdateIntervalMilliseconds = Clamp(ServerTelemetryUpdateIntervalMs, 250, 60000)
            },
            Security = new OpcUaSecurityOptions
            {
                Mode = "None",
                Authentication = new OpcUaAuthenticationOptions { Mode = "Anonymous" },
                Certificates = new OpcUaCertificateOptions { Enabled = false }
            },
            Nodes = BuildNodeOptions(),
            KnownEndpoints = _knownEndpoints.Select(endpoint => endpoint.Clone()).ToList()
        };
    }

    private OpcUaNodeOptions BuildNodeOptions()
    {
        return new OpcUaNodeOptions
        {
            NamespaceUri = Normalize(NamespaceUri, "urn:Configurator:OpcUa:Demo"),
            UseConfiguredTags = true,
            ServerTelemetryTags = CurrentServerTelemetryTags(),
            ClientTelemetryTags = CurrentClientTelemetryTags(),
            ServerCommandTags = CurrentServerCommandTags(),
            ClientCommandTags = CurrentClientCommandTags()
        };
    }

    private void ApplyOptions(OpcUaOptions options)
    {
        _knownEndpoints.Clear();
        _knownEndpoints.AddRange(options.KnownEndpoints.Select(endpoint => endpoint.Clone()));
        ClientEnabled = options.Client.Enabled;
        ServerEnabled = options.Server.Enabled;
        ServerDemoTelemetryEnabled = options.Server.DemoTelemetryEnabled;
        ClientEndpointUrl = options.Client.EndpointUrl;
        ServerEndpointUrl = options.Server.EndpointUrl;
        ServerTelemetryUpdateIntervalMs = options.Server.TelemetryUpdateIntervalMilliseconds;
        NamespaceUri = options.Nodes.NamespaceUri;
        CertificatesEnabled = options.Security.Certificates.Enabled;
        RebuildRowsFromDefinitions(
            options.Nodes.ServerTelemetryDefinitions(),
            options.Nodes.ClientTelemetryDefinitions(),
            options.Nodes.ServerCommandDefinitions(),
            options.Nodes.ClientCommandDefinitions());
    }

    private void RebuildRowsFromDefinitions(
        IEnumerable<OpcUaConfiguredTag> serverTelemetryTags,
        IEnumerable<OpcUaConfiguredTag> clientTelemetryTags,
        IEnumerable<OpcUaConfiguredTag> serverCommandTags,
        IEnumerable<OpcUaConfiguredTag> clientCommandTags)
    {
        // При перестройке строк создаются новые OpcUaTagRow с актуальными write callbacks,
        // чтобы права записи соответствовали текущему состоянию runtime.
        ClientTelemetryRows.Clear();
        ClientCommandRows.Clear();
        ServerTelemetryRows.Clear();
        ServerCommandRows.Clear();

        foreach (var tag in clientTelemetryTags.Select(tag => tag.Clone()))
        {
            ClientTelemetryRows.Add(new OpcUaTagRow(tag, false));
        }

        foreach (var tag in clientCommandTags.Select(tag => tag.Clone()))
        {
            ClientCommandRows.Add(new OpcUaTagRow(tag, CanWriteClientCommands, WriteClientCommandAsync));
        }

        foreach (var tag in serverTelemetryTags.Select(tag => tag.Clone()))
        {
            ServerTelemetryRows.Add(new OpcUaTagRow(tag, CanWriteServerTelemetry, UpdateServerTelemetryAsync));
        }

        foreach (var tag in serverCommandTags.Select(tag => tag.Clone()))
        {
            ServerCommandRows.Add(new OpcUaTagRow(tag, false));
        }

        SelectedServerTelemetryRow = null;
        SelectedClientTelemetryRow = null;
        SelectedClientCommandRow = null;
        ApplyRowWriteState();
    }

    private List<OpcUaConfiguredTag> CurrentServerTelemetryTags()
        => ServerTelemetryRows.Select(row => row.ToConfiguredTag()).ToList();

    private List<OpcUaConfiguredTag> CurrentClientTelemetryTags()
        => ClientTelemetryRows.Select(row => row.ToConfiguredTag()).ToList();

    private List<OpcUaConfiguredTag> CurrentServerCommandTags()
        => ServerCommandRows.Select(row => row.ToConfiguredTag()).ToList();

    private List<OpcUaConfiguredTag> CurrentClientCommandTags()
        => ClientCommandRows.Select(row => row.ToConfiguredTag()).ToList();

    private void SaveOpcUaOptions()
    {
        try
        {
            var settings = _appConfigService.LoadUserSettings();
            settings.OpcUa = BuildOptions();
            _appConfigService.SaveUserSettings(settings);
        }
        catch (Exception ex)
        {
            SetLastError(ex.Message);
            AddEvent("Error", "Failed to save OPC UA settings", "Error", ex.ToString());
        }
    }

    private void OnRuntimeStatusChanged(object? sender, OpcUaStatus status)
    {
        Dispatcher.UIThread.Post(() => ApplyStatus(status));
    }

    private void OnRuntimeSnapshotChanged(object? sender, OpcUaSnapshot snapshot)
    {
        Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
    }

    private void ApplyStatus(OpcUaStatus status)
    {
        ClientState = status.ClientState.ToString();
        ServerState = status.ServerState.ToString();
        ClientMessage = status.ClientMessage;
        ServerMessage = status.ServerMessage;
        SetLastError(status.LastError);
        LastUpdated = status.UpdatedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        AddEvent("Client", $"{status.ClientState} - {status.ClientMessage}");
        AddEvent("Server", $"{status.ServerState} - {status.ServerMessage}");

        if (!string.IsNullOrWhiteSpace(status.LastError))
        {
            AddEvent("Error", LastError, "Error", status.LastError);
        }
    }

    private void ApplySnapshot(OpcUaSnapshot snapshot)
    {
        if (snapshot.Role == OpcUaRuntimeRole.Client)
        {
            ApplyValues(ClientTelemetryRows, snapshot.TelemetryValues);
            ApplyValues(ClientCommandRows, snapshot.CommandValues);
            return;
        }

        if (snapshot.Role == OpcUaRuntimeRole.Server)
        {
            ApplyValues(ServerTelemetryRows, snapshot.TelemetryValues);
            ApplyValues(ServerCommandRows, snapshot.CommandValues);
        }
    }

    private static void ApplyValues(IEnumerable<OpcUaTagRow> rows, IReadOnlyList<OpcUaTagValue> values)
    {
        // Значения сопоставляются по SDK-независимому адресу, а не по имени:
        // имя пользователь может переименовать при ручном редактировании.
        foreach (var value in values)
        {
            var row = rows.FirstOrDefault(candidate => SameAddress(candidate.Address, value.Address));
            row?.SetFromValue(value);
        }
    }

    private void ReportOperationError(OpcUaOperationResult result)
    {
        var message = OperationMessage(result, "OPC UA operation failed.");
        SetLastError(message);
        AddEvent("Error", LastError, "Error", message);
    }

    private static string OperationMessage(OpcUaOperationResult result, string fallback)
        => result.Error?.Details is { Length: > 0 } details
            ? $"{result.Error.Message} {details}"
            : result.Error?.Message ?? fallback;

    private void AddEvent(string source, string message, string level = "Info", string details = "")
    {
        var now = DateTimeOffset.Now;
        var shortMessage = TrimForUi(message);
        var key = $"{source}|{level}|{shortMessage}";

        if (_eventTimes.TryGetValue(key, out var lastEventAt)
            && now - lastEventAt < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _eventTimes[key] = now;
        Events.Insert(0, new OpcUaEventRow(now, source, level, shortMessage, details));

        while (Events.Count > 200)
        {
            Events.RemoveAt(Events.Count - 1);
        }
    }

    private void SetLastError(string? message)
    {
        LastError = TrimForUi(message ?? string.Empty);
    }

    private void RaiseCommandStateChanged()
    {
        this.RaisePropertyChanged(nameof(CanStartClient));
        this.RaisePropertyChanged(nameof(CanStopClient));
        this.RaisePropertyChanged(nameof(CanRestartClient));
        this.RaisePropertyChanged(nameof(CanStartServer));
        this.RaisePropertyChanged(nameof(CanStopServer));
        this.RaisePropertyChanged(nameof(CanRestartServer));
        this.RaisePropertyChanged(nameof(CanStartBoth));
        this.RaisePropertyChanged(nameof(CanStopBoth));
        this.RaisePropertyChanged(nameof(CanRestartBoth));
        this.RaisePropertyChanged(nameof(CanModifyTags));
        this.RaisePropertyChanged(nameof(CanWriteClientCommands));
        this.RaisePropertyChanged(nameof(CanWriteServerTelemetry));
        ApplyRowWriteState();
    }

    private void ApplyRowWriteState()
    {
        foreach (var row in ClientCommandRows)
        {
            row.CanWrite = CanWriteClientCommands;
        }

        foreach (var row in ServerTelemetryRows)
        {
            row.CanWrite = CanWriteServerTelemetry;
        }
    }

    private OpcUaConfiguredTag CreateManualTag(OpcUaImportTarget target, bool clientSide = false)
    {
        var isTelemetry = target == OpcUaImportTarget.ServerTelemetry;
        var index = isTelemetry
            ? (clientSide ? ClientTelemetryRows.Count : ServerTelemetryRows.Count) + 1
            : ClientCommandRows.Count + 1;
        var folder = isTelemetry ? "Telemetry" : "Commands";
        var name = isTelemetry ? $"Telemetry{index}" : $"Command{index}";

        return new OpcUaConfiguredTag
        {
            Name = name,
            Address = new OpcUaTagAddress(
                Normalize(NamespaceUri, "urn:Configurator:OpcUa:Demo"),
                $"DemoDevice/{folder}/{name}",
                "String",
                "String"),
            Access = isTelemetry ? OpcUaTagAccess.Read : OpcUaTagAccess.ReadWrite,
            InitialValue = string.Empty
        };
    }

    private OpcUaConfiguredTag ToLocalServerTelemetryTag(OpcUaConfiguredTag imported)
    {
        var name = Normalize(imported.Name, "Telemetry");
        return new OpcUaConfiguredTag
        {
            Name = name,
            Address = new OpcUaTagAddress(
                Normalize(NamespaceUri, "urn:Configurator:OpcUa:Demo"),
                $"DemoDevice/Telemetry/{SafeIdentifier(name)}",
                imported.Address.DataType,
                "String"),
            Access = OpcUaTagAccess.Read,
            Comment = imported.Comment,
            InitialValue = string.IsNullOrWhiteSpace(imported.InitialValue)
                ? OpcUaDataTypeSupport.DefaultInitialValue(imported.Address.DataType)
                : imported.InitialValue
        };
    }

    private void ApplyKnownEndpoints(IEnumerable<OpcUaEndpointProfile> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.EndpointUrl))
            {
                continue;
            }

            var existing = _knownEndpoints.FirstOrDefault(candidate =>
                string.Equals(candidate.EndpointUrl, endpoint.EndpointUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _knownEndpoints.Add(endpoint.Clone());
                continue;
            }

            existing.Name = string.IsNullOrWhiteSpace(endpoint.Name) ? existing.Name : endpoint.Name;
            existing.Source = string.IsNullOrWhiteSpace(endpoint.Source) ? existing.Source : endpoint.Source;
            existing.LastConnectedAt = endpoint.LastConnectedAt ?? existing.LastConnectedAt;
        }
    }

    private void ApplySelectedClientEndpoint(OpcUaEndpointProfile? endpoint)
    {
        if (!string.IsNullOrWhiteSpace(endpoint?.EndpointUrl))
        {
            ClientEndpointUrl = endpoint.EndpointUrl.Trim();
        }
    }

    private static List<OpcUaConfiguredTag> MergeTags(
        IEnumerable<OpcUaConfiguredTag> current,
        IEnumerable<OpcUaConfiguredTag> imported)
    {
        var tags = current.Select(tag => tag.Clone()).ToList();
        var keys = new HashSet<string>(
            tags.Select(tag => TagKey(tag.Address)),
            StringComparer.Ordinal);

        foreach (var tag in imported)
        {
            if (keys.Add(TagKey(tag.Address)))
            {
                tags.Add(tag.Clone());
            }
        }

        return tags;
    }

    private static bool IsRoleRunning(string value)
    {
        var state = ParseState(value);
        return state is OpcUaConnectionStatus.Connecting
            or OpcUaConnectionStatus.Connected
            or OpcUaConnectionStatus.Reconnecting;
    }

    private static OpcUaConnectionStatus ParseState(string value)
    {
        return Enum.TryParse<OpcUaConnectionStatus>(value, out var state)
            ? state
            : OpcUaConnectionStatus.Disconnected;
    }

    private static bool IsStartable(OpcUaConnectionStatus state)
    {
        return state is OpcUaConnectionStatus.Disconnected or OpcUaConnectionStatus.Faulted;
    }

    private static bool IsStoppable(OpcUaConnectionStatus state)
    {
        return state is OpcUaConnectionStatus.Connecting or OpcUaConnectionStatus.Connected or OpcUaConnectionStatus.Reconnecting;
    }

    private static string NormalizeEndpoint(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "opc.tcp://localhost:4840" : value.Trim();
    }

    private static string Normalize(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string SafeIdentifier(string value)
    {
        var chars = value
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray();
        var identifier = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(identifier) ? "Tag" : identifier;
    }

    private static string TrimForUi(string message)
    {
        const int maxLength = 180;

        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var singleLine = message.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, maxLength), "...");
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static bool SameEndpoint(string left, string right)
        => string.Equals(NormalizeEndpoint(left), NormalizeEndpoint(right), StringComparison.OrdinalIgnoreCase);

    private static bool SameAddress(OpcUaTagAddress left, OpcUaTagAddress right)
        => string.Equals(left.NamespaceUri, right.NamespaceUri, StringComparison.Ordinal)
           && string.Equals(left.Identifier, right.Identifier, StringComparison.Ordinal)
           && string.Equals(left.IdentifierType, right.IdentifierType, StringComparison.OrdinalIgnoreCase);

    private static string TagKey(OpcUaTagAddress address)
        => $"{address.NamespaceUri}|{address.IdentifierType}|{address.Identifier}";
}

/// <summary>
/// Строка журнала OPC UA экрана с коротким сообщением и подробностями для tooltip.
/// </summary>
public sealed class OpcUaEventRow
{
    /// <summary>
    /// Создает строку журнала.
    /// </summary>
    public OpcUaEventRow(DateTimeOffset timestamp, string source, string level, string message, string details)
    {
        Timestamp = timestamp;
        Source = source;
        Level = level;
        Message = message;
        Details = string.IsNullOrWhiteSpace(details) ? message : details;
    }

    public DateTimeOffset Timestamp { get; }

    public string Source { get; }

    public string Level { get; }

    public string Message { get; }

    public string Details { get; }

    /// <summary>
    /// Время события в формате, удобном для компактной таблицы.
    /// </summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
