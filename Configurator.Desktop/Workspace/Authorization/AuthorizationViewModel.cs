using Configurator.Application.Services.Authorization;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.Authorization;

public sealed class AuthorizationViewModel : ViewModelBase, IRoutableViewModel, IActivatableViewModel, IDisposable
{
    private const string GenericErrorMessage = "Invalid username or password.";

    private readonly IAuthenticationService _authenticationService;
    private readonly ILoginCredentialStore _loginCredentialStore;
    private readonly Func<CancellationToken, Task> _authenticationSucceeded;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CompositeDisposable _disposables = new();
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string? _errorMessage;
    private bool _isBusy;
    private bool _rememberPassword;
    private bool _autoLogin;
    private bool _initialized;
    private bool _autoLoginAttempted;
    private bool _disposed;

    public AuthorizationViewModel(
        IScreen hostScreen,
        IAuthenticationService authenticationService,
        Func<CancellationToken, Task> authenticationSucceeded,
        ILoginCredentialStore? loginCredentialStore = null)
    {
        HostScreen = hostScreen;
        _authenticationService = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
        _loginCredentialStore = loginCredentialStore ?? new NullLoginCredentialStore();
        _authenticationSucceeded = authenticationSucceeded ?? throw new ArgumentNullException(nameof(authenticationSucceeded));

        AuthenticateCommand = ReactiveCommand.CreateFromTask(AuthenticateAsync);
        _disposables.Add(AuthenticateCommand.IsExecuting.Subscribe(value => IsBusy = value));
    }

    public string UrlPathSegment => "login";

    public IScreen HostScreen { get; }

    public Interaction<string, Unit> ErrorInteraction { get; } = new();

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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public bool RememberPassword
    {
        get => _rememberPassword;
        set
        {
            this.RaiseAndSetIfChanged(ref _rememberPassword, value);
            if (!value && AutoLogin)
            {
                AutoLogin = false;
            }

            this.RaisePropertyChanged(nameof(CanUseAutoLogin));
        }
    }

    public bool AutoLogin
    {
        get => _autoLogin;
        set => this.RaiseAndSetIfChanged(ref _autoLogin, RememberPassword && value);
    }

    public bool CanUseAutoLogin => RememberPassword;

    public ReactiveCommand<Unit, Unit> AuthenticateCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        var preferences = await _loginCredentialStore.LoadAsync(cancellationToken);
        RememberPassword = preferences.RememberPassword;
        AutoLogin = preferences.AutoLogin;
        Username = preferences.Username;
        Password = RememberPassword ? preferences.Password ?? string.Empty : string.Empty;

        if (AutoLogin)
        {
            await TryAutoLoginAsync(cancellationToken);
        }
    }

    public async Task TryAutoLoginAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_autoLoginAttempted)
        {
            return;
        }

        _autoLoginAttempted = true;
        if (!RememberPassword || !AutoLogin || string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
        {
            return;
        }

        await AuthenticateCoreAsync(isAutoLogin: true, cancellationToken);
    }

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

    private async Task AuthenticateAsync()
        => await AuthenticateCoreAsync(isAutoLogin: false, _lifetimeCancellation.Token);

    private async Task AuthenticateCoreAsync(bool isAutoLogin, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            {
                await ShowErrorAsync(GenericErrorMessage);
                return;
            }

            var result = await _authenticationService
                .AuthenticateAsync(new AuthenticationRequest(Username, Password), cancellationToken);
            if (result.Succeeded)
            {
                ErrorMessage = null;
                if (RememberPassword)
                {
                    await _loginCredentialStore.SaveAsync(
                        new LoginCredentialPreferences(Username, Password, true, AutoLogin),
                        cancellationToken);
                }
                else
                {
                    await _loginCredentialStore.ClearAsync(cancellationToken);
                }

                await _authenticationSucceeded(cancellationToken);
                return;
            }

            if (isAutoLogin)
            {
                RememberPassword = false;
                Password = string.Empty;
                await _loginCredentialStore.ClearAsync(cancellationToken);
            }

            await ShowErrorAsync(GenericErrorMessage);
        }
        finally
        {
            Password = string.Empty;
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        ErrorMessage = message;
        try
        {
            await ErrorInteraction.Handle(message).FirstAsync();
        }
        catch (Exception ex) when (ex.GetType().Name == "UnhandledInteractionException")
        {
        }
    }
}
