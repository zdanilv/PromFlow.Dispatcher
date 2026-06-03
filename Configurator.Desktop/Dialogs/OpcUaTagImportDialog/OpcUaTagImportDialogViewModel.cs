using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;

namespace Configurator.Desktop.Dialogs.OpcUaTagImportDialog;

/// <summary>
/// Модель представления диалога импорта OPC UA тегов: выбирает endpoint, выполняет поиск/обход и возвращает выбранные теги.
/// </summary>
public sealed class OpcUaTagImportDialogViewModel : ReactiveObject
{
    private readonly IOpcUaTagBrowserService _browserService;
    private readonly OpcUaBrowseRequest _request;
    private readonly Subject<OpcUaTagImportResult?> _result = new();
    private bool _canSubmit;
    private string _endpointUrl;
    private bool _isBusy;
    private OpcUaEndpointOption? _selectedEndpoint;
    private string _statusText = "Press Connect to read OPC UA tags.";

    /// <summary>
    /// Создает диалог импорта для конкретного назначения и стартового endpoint.
    /// </summary>
    public OpcUaTagImportDialogViewModel(
        IOpcUaTagBrowserService browserService,
        OpcUaBrowseRequest request)
    {
        _browserService = browserService;
        _request = request;
        _endpointUrl = request.EndpointUrl;
        SeedEndpoints(request);

        AddEndpointCommand = ReactiveCommand.Create(AddEndpoint, this.WhenAnyValue(x => x.IsNotBusy));
        DiscoverCommand = ReactiveCommand.CreateFromTask(DiscoverAsync, this.WhenAnyValue(x => x.IsNotBusy));
        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync, this.WhenAnyValue(x => x.IsNotBusy));
        OkCommand = ReactiveCommand.Create(Submit, this.WhenAnyValue(x => x.CanSubmit));
        CancelCommand = ReactiveCommand.Create(() => _result.OnNext(null));
    }

    /// <summary>
    /// Endpoint-адреса из настроек, поиска и ручного ввода.
    /// </summary>
    public ObservableCollection<OpcUaEndpointOption> EndpointOptions { get; } = [];

    /// <summary>
    /// Дерево узлов, прочитанное из выбранного OPC UA endpoint.
    /// </summary>
    public ObservableCollection<OpcUaImportNodeViewModel> Nodes { get; } = [];

    public ReactiveCommand<Unit, Unit> AddEndpointCommand { get; }

    public ReactiveCommand<Unit, Unit> DiscoverCommand { get; }

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    public ReactiveCommand<Unit, Unit> OkCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public IObservable<OpcUaTagImportResult?> Result => _result;

    public bool IsNotBusy => !IsBusy;

    public bool CanSubmit
    {
        get => _canSubmit;
        private set => this.RaiseAndSetIfChanged(ref _canSubmit, value);
    }

    public OpcUaEndpointOption? SelectedEndpoint
    {
        get => _selectedEndpoint;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEndpoint, value);
            if (value is not null)
            {
                EndpointUrl = value.Profile.EndpointUrl;
            }
        }
    }

    public string EndpointUrl
    {
        get => _endpointUrl;
        set => this.RaiseAndSetIfChanged(ref _endpointUrl, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(IsNotBusy));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private void SeedEndpoints(OpcUaBrowseRequest request)
    {
        // Диалог всегда стартует с нескольких кандидатов, чтобы пользователь мог
        // быстро переключиться между клиентским, серверным и ручным endpoint.
        AddEndpointOption(request.Options.Client.EndpointUrl, "Client");
        AddEndpointOption(request.Options.Server.EndpointUrl, "Server");
        AddEndpointOption(request.EndpointUrl, "Manual");

        foreach (var endpoint in request.Options.KnownEndpoints)
        {
            AddEndpointOption(endpoint.EndpointUrl, endpoint.Source, endpoint.LastConnectedAt, endpoint.Name);
        }

        SelectedEndpoint = EndpointOptions.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Profile.EndpointUrl, request.EndpointUrl, StringComparison.OrdinalIgnoreCase))
                           ?? EndpointOptions.FirstOrDefault();
    }

    private void AddEndpoint()
    {
        var endpoint = NormalizeEndpoint(EndpointUrl);
        AddEndpointOption(endpoint, "Manual");
        SelectedEndpoint = EndpointOptions.FirstOrDefault(item =>
            string.Equals(item.Profile.EndpointUrl, endpoint, StringComparison.OrdinalIgnoreCase));
        StatusText = $"Endpoint added: {endpoint}";
    }

    private async Task DiscoverAsync()
    {
        // Поиск расширяет список endpoint-ов, но не очищает ручные и ранее сохраненные адреса.
        IsBusy = true;
        StatusText = "Discovering endpoints...";

        try
        {
            var endpoint = NormalizeEndpoint(EndpointUrl);
            var result = await _browserService.DiscoverEndpointsAsync(new OpcUaEndpointDiscoveryRequest
            {
                EndpointUrl = endpoint,
                Options = _request.Options
            });

            if (!result.Succeeded || result.Value is null)
            {
                StatusText = result.Error?.Message ?? "Endpoint discovery failed.";
                return;
            }

            var selectedUrl = SelectedEndpoint?.Profile.EndpointUrl;
            foreach (var profile in result.Value.Endpoints)
            {
                AddEndpointOption(
                    profile.EndpointUrl,
                    profile.Source,
                    profile.LastConnectedAt,
                    profile.Name);
            }

            SelectedEndpoint = EndpointOptions.FirstOrDefault(item =>
                                   string.Equals(item.Profile.EndpointUrl, selectedUrl, StringComparison.OrdinalIgnoreCase))
                               ?? EndpointOptions.FirstOrDefault(item =>
                                   string.Equals(item.Profile.EndpointUrl, endpoint, StringComparison.OrdinalIgnoreCase))
                               ?? EndpointOptions.FirstOrDefault();
            StatusText = string.IsNullOrWhiteSpace(result.Value.Status)
                ? $"Endpoints found: {EndpointOptions.Count}."
                : result.Value.Status;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ConnectAsync()
    {
        // Обход выполняется временной сессией сервиса просмотра и не меняет текущий runtime.
        IsBusy = true;
        StatusText = "Connecting...";

        try
        {
            _request.EndpointUrl = NormalizeEndpoint(SelectedEndpoint?.Profile.EndpointUrl ?? EndpointUrl);
            AddEndpointOption(_request.EndpointUrl, "Manual");

            var result = await _browserService.BrowseAsync(_request);
            Nodes.Clear();
            CanSubmit = false;

            if (!result.Succeeded || result.Value is null)
            {
                StatusText = result.Error?.Message ?? "Browse failed.";
                return;
            }

            foreach (var node in result.Value.Nodes)
            {
                Nodes.Add(new OpcUaImportNodeViewModel(node, RefreshSubmitState));
            }

            MarkEndpointConnected(_request.EndpointUrl);
            StatusText = result.Value.Status;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Submit()
    {
        // Возвращаем только selectable variable nodes; папки дерева нужны только для навигации.
        var tags = Nodes
            .SelectMany(node => node.SelectedNodes())
            .Where(node => node.Address is not null)
            .Select(ToConfiguredTag)
            .ToArray();

        if (tags.Length == 0)
        {
            StatusText = "Select at least one supported tag.";
            return;
        }

        _result.OnNext(new OpcUaTagImportResult
        {
            Tags = tags,
            SelectedEndpoint = SelectedEndpoint?.Profile,
            KnownEndpoints = EndpointOptions.Select(endpoint => endpoint.Profile.Clone()).ToArray()
        });
    }

    private OpcUaConfiguredTag ToConfiguredTag(OpcUaBrowseNode node)
    {
        return new OpcUaConfiguredTag
        {
            Name = node.Name,
            Address = new OpcUaTagAddress(
                node.Address!.NamespaceUri,
                node.Address.Identifier,
                node.Address.DataType,
                node.Address.IdentifierType),
            Access = node.Access,
            Comment = node.Comment,
            InitialValue = OpcUaDataTypeSupport.DefaultInitialValue(node.NormalizedDataType)
        };
    }

    private void RefreshSubmitState()
    {
        CanSubmit = Nodes.SelectMany(node => node.SelectedNodes()).Any();
        if (!CanSubmit && Nodes.Count > 0)
        {
            StatusText = "Select at least one supported tag.";
        }
    }

    private void AddEndpointOption(
        string endpointUrl,
        string source,
        DateTimeOffset? lastConnectedAt = null,
        string? name = null)
    {
        var normalized = NormalizeEndpoint(endpointUrl);
        var existing = EndpointOptions.FirstOrDefault(item =>
            string.Equals(item.Profile.EndpointUrl, normalized, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            if (lastConnectedAt is not null)
            {
                existing.Profile.LastConnectedAt = lastConnectedAt;
            }

            return;
        }

        EndpointOptions.Add(new OpcUaEndpointOption(new OpcUaEndpointProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? EndpointName(normalized) : name.Trim(),
            EndpointUrl = normalized,
            Source = source,
            LastConnectedAt = lastConnectedAt
        }));
    }

    private void MarkEndpointConnected(string endpointUrl)
    {
        var normalized = NormalizeEndpoint(endpointUrl);
        var endpoint = EndpointOptions.FirstOrDefault(item =>
            string.Equals(item.Profile.EndpointUrl, normalized, StringComparison.OrdinalIgnoreCase));

        if (endpoint is null)
        {
            AddEndpointOption(normalized, "Manual", DateTimeOffset.Now);
            endpoint = EndpointOptions.FirstOrDefault(item =>
                string.Equals(item.Profile.EndpointUrl, normalized, StringComparison.OrdinalIgnoreCase));
        }

        if (endpoint is not null)
        {
            endpoint.Profile.LastConnectedAt = DateTimeOffset.Now;
            SelectedEndpoint = endpoint;
        }
    }

    private static string NormalizeEndpoint(string value)
        => string.IsNullOrWhiteSpace(value) ? "opc.tcp://localhost:4840" : value.Trim();

    private static string EndpointName(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return endpointUrl;
        }

        return $"{uri.Host}:{uri.Port}";
    }
}

/// <summary>
/// UI-обертка над профилем endpoint для выпадающего списка.
/// </summary>
public sealed class OpcUaEndpointOption
{
    /// <summary>
    /// Создает элемент выбора из сохраненного профиля endpoint.
    /// </summary>
    public OpcUaEndpointOption(OpcUaEndpointProfile profile)
    {
        Profile = profile;
    }

    public OpcUaEndpointProfile Profile { get; }

    /// <summary>
    /// Текст для отображения в списке endpoint-ов.
    /// </summary>
    public string Display => string.IsNullOrWhiteSpace(Profile.Name)
        ? Profile.EndpointUrl
        : $"{Profile.Name} - {Profile.EndpointUrl}";

    public override string ToString()
        => Display;
}
