using System.Reactive;
using System.Reactive.Disposables;
using Avalonia.Threading;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Licensing;

public sealed class LicenseViewModel : ViewModelBase, IDisposable
{
    private readonly ILicenseService _licenseService;
    private readonly ILicenseStateAccessor _licenseStateAccessor;
    private readonly IInstallationIdentityService _installationIdentityService;
    private readonly ILicenseRequestExportService _requestExportService;
    private readonly ILicenseFilePicker _filePicker;
    private readonly IAccessDecisionService _accessDecisionService;
    private readonly LicensingOptions _options;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CompositeDisposable _disposables = new();
    private string _status = LicenseStatus.Missing.ToString();
    private string _reasonCode = string.Empty;
    private string _licenseId = string.Empty;
    private string _edition = string.Empty;
    private string _validFromUtc = string.Empty;
    private string _expiresAtUtc = string.Empty;
    private string _organization = string.Empty;
    private string _site = string.Empty;
    private string _customer = string.Empty;
    private string _productVersion = string.Empty;
    private string _features = string.Empty;
    private string _installationId = string.Empty;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isBusy;
    private bool _disposed;

    public LicenseViewModel(
        ILicenseService licenseService,
        ILicenseStateAccessor licenseStateAccessor,
        IInstallationIdentityService installationIdentityService,
        ILicenseRequestExportService requestExportService,
        ILicenseFilePicker filePicker,
        IAccessDecisionService accessDecisionService,
        LicensingOptions options)
    {
        _licenseService = licenseService ?? throw new ArgumentNullException(nameof(licenseService));
        _licenseStateAccessor = licenseStateAccessor ?? throw new ArgumentNullException(nameof(licenseStateAccessor));
        _installationIdentityService = installationIdentityService ?? throw new ArgumentNullException(nameof(installationIdentityService));
        _requestExportService = requestExportService ?? throw new ArgumentNullException(nameof(requestExportService));
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _accessDecisionService = accessDecisionService ?? throw new ArgumentNullException(nameof(accessDecisionService));
        _options = options?.Clone() ?? throw new ArgumentNullException(nameof(options));

        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync);
        ExportRequestCommand = ReactiveCommand.CreateFromTask(ExportRequestAsync);
        _disposables.Add(RefreshCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(InstallCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(ExportRequestCommand.IsExecuting.Subscribe(UpdateBusy));

        _licenseStateAccessor.StateChanged += OnLicenseStateChanged;
        ApplyState(_licenseStateAccessor.Current);
    }

    public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
    public string ReasonCode { get => _reasonCode; private set => this.RaiseAndSetIfChanged(ref _reasonCode, value); }
    public string LicenseId { get => _licenseId; private set => this.RaiseAndSetIfChanged(ref _licenseId, value); }
    public string Edition { get => _edition; private set => this.RaiseAndSetIfChanged(ref _edition, value); }
    public string ValidFromUtc { get => _validFromUtc; private set => this.RaiseAndSetIfChanged(ref _validFromUtc, value); }
    public string ExpiresAtUtc { get => _expiresAtUtc; private set => this.RaiseAndSetIfChanged(ref _expiresAtUtc, value); }
    public string Organization { get => _organization; private set => this.RaiseAndSetIfChanged(ref _organization, value); }
    public string Site { get => _site; private set => this.RaiseAndSetIfChanged(ref _site, value); }
    public string Customer { get => _customer; private set => this.RaiseAndSetIfChanged(ref _customer, value); }
    public string ProductVersion { get => _productVersion; private set => this.RaiseAndSetIfChanged(ref _productVersion, value); }
    public string Features { get => _features; private set => this.RaiseAndSetIfChanged(ref _features, value); }
    public string InstallationId { get => _installationId; private set => this.RaiseAndSetIfChanged(ref _installationId, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }
    public string? StatusMessage { get => _statusMessage; private set => this.RaiseAndSetIfChanged(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportRequestCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _licenseStateAccessor.StateChanged -= OnLicenseStateChanged;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _disposables.Dispose();
    }

    private async Task RefreshAsync()
    {
        var state = await _licenseService.RefreshAsync(_lifetimeCancellation.Token);
        ApplyState(state);
        ErrorMessage = null;
        RaiseHasErrorChanged();
        StatusMessage = "License state refreshed.";
    }

    private async Task InstallAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        var decision = await _accessDecisionService
            .AuthorizeAsync(new AccessRequirement(Permission.InstallLicense), cancellationToken);
        if (!decision.Succeeded)
        {
            SetError($"Install license permission is required. Reason: {decision.ReasonCode}");
            return;
        }

        var path = await _filePicker.PickLicensePathAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var maxBytes = Math.Max(1, _options.MaxLicenseFileBytes);
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > maxBytes)
        {
            SetError($"License file is larger than {maxBytes} bytes.");
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var result = await _licenseService.InstallAsync(
            new LicenseInstallRequest(bytes, Path.GetFileName(path)),
            cancellationToken);
        if (!result.Succeeded)
        {
            ApplyState(result.State);
            SetError(result.Message);
            return;
        }

        ApplyState(result.State);
        ErrorMessage = null;
        RaiseHasErrorChanged();
        StatusMessage = "License installed.";
    }

    private async Task ExportRequestAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        var decision = await _accessDecisionService
            .AuthorizeAsync(new AccessRequirement(Permission.ViewLicense), cancellationToken);
        if (!decision.Succeeded)
        {
            SetError($"View license permission is required. Reason: {decision.ReasonCode}");
            return;
        }

        var path = await _filePicker.PickRequestExportPathAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var request = await _installationIdentityService.CreateRequestAsync(cancellationToken);
        var result = await _requestExportService.ExportAsync(request, path, cancellationToken);
        if (!result.Succeeded)
        {
            SetError(result.ErrorMessage ?? "Installation request export failed.");
            return;
        }

        InstallationId = request.InstallationId;
        ErrorMessage = null;
        RaiseHasErrorChanged();
        StatusMessage = $"Installation request exported: {result.Path}";
    }

    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ApplyState(e.Current));
    }

    private void ApplyState(LicenseState state)
    {
        Status = state.Status.ToString();
        ReasonCode = state.PrimaryErrorCode?.ToString() ?? string.Empty;
        LicenseId = state.Payload?.LicenseId.ToString("D") ?? string.Empty;
        Edition = state.Payload?.Edition.ToString() ?? string.Empty;
        ValidFromUtc = FormatDate(state.Payload?.ValidFromUtc);
        ExpiresAtUtc = FormatDate(state.Payload?.ExpiresAtUtc);
        Organization = state.Payload?.Organization.Name ?? string.Empty;
        Site = state.Payload?.Organization.SiteAddress ?? string.Empty;
        Customer = state.Payload?.Customer.FullName ?? string.Empty;
        ProductVersion = state.Payload is null
            ? string.Empty
            : $"{state.Payload.ProductVersion.Minimum} - {state.Payload.ProductVersion.MaximumExclusive}";
        Features = state.Payload is null ? string.Empty : string.Join(", ", state.Payload.Features.Order(StringComparer.Ordinal));
        InstallationId = state.Payload?.Installation.InstallationId ?? InstallationId;
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        RaiseHasErrorChanged();
        StatusMessage = null;
    }

    private void RaiseHasErrorChanged() => this.RaisePropertyChanged(nameof(HasError));

    private void UpdateBusy(bool value) => IsBusy = value;

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("u") ?? string.Empty;
}
