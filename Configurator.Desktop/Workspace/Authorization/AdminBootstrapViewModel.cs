using Configurator.Application.Services.Authorization;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.Authorization;

public sealed class AdminBootstrapViewModel : ViewModelBase, IRoutableViewModel, IActivatableViewModel, IDisposable
{
    private readonly IUserManagementService _userManagementService;
    private readonly Func<CancellationToken, Task> _bootstrapSucceeded;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CompositeDisposable _disposables = new();
    private string _username = "admin";
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _disposed;

    public AdminBootstrapViewModel(
        IScreen hostScreen,
        IUserManagementService userManagementService,
        Func<CancellationToken, Task> bootstrapSucceeded)
    {
        HostScreen = hostScreen;
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));
        _bootstrapSucceeded = bootstrapSucceeded ?? throw new ArgumentNullException(nameof(bootstrapSucceeded));

        BootstrapCommand = ReactiveCommand.CreateFromTask(BootstrapAsync);
        _disposables.Add(BootstrapCommand.IsExecuting.Subscribe(value => IsBusy = value));
    }

    public string UrlPathSegment => "bootstrap";

    public IScreen HostScreen { get; }

    public ViewModelActivator Activator { get; } = new();

    public string Username
    {
        get => _username;
        set => this.RaiseAndSetIfChanged(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => this.RaiseAndSetIfChanged(ref _confirmPassword, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> BootstrapCommand { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        _disposables.Dispose();
    }

    private async Task BootstrapAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        try
        {
            if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "Username and password are required.";
                return;
            }

            var result = await _userManagementService
                .BootstrapAdministratorAsync(
                    new BootstrapAdministratorRequest(Username, Password),
                    cancellationToken);
            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorMessage ?? "Administrator bootstrap failed.";
                return;
            }

            ErrorMessage = null;
            await _bootstrapSucceeded(cancellationToken);
        }
        finally
        {
            Password = string.Empty;
            ConfirmPassword = string.Empty;
        }
    }
}
