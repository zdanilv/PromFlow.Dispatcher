using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using Configurator.Application.Services.Authorization;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Users;

public sealed class UserManagementViewModel : ViewModelBase, IDisposable
{
    private readonly IUserManagementService _userManagementService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly CompositeDisposable _disposables = new();
    private UserRowViewModel? _selectedUser;
    private string _newUsername = string.Empty;
    private UserRole _newRole = UserRole.User;
    private bool _newUserEnabled = true;
    private string _newPassword = string.Empty;
    private string _selectedNewPassword = string.Empty;
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _isBusy;
    private bool _disposed;

    public UserManagementViewModel(IUserManagementService userManagementService)
    {
        _userManagementService = userManagementService ?? throw new ArgumentNullException(nameof(userManagementService));

        InitializeCommand = ReactiveCommand.CreateFromTask(() => InitializeAsync(_lifetimeCancellation.Token));
        RefreshCommand = ReactiveCommand.CreateFromTask(() => RefreshAsync());
        CreateUserCommand = ReactiveCommand.CreateFromTask(CreateUserAsync);
        ChangePasswordCommand = ReactiveCommand.CreateFromTask(ChangePasswordAsync);
        EnableSelectedCommand = ReactiveCommand.CreateFromTask(() => SetSelectedEnabledAsync(true));
        DisableSelectedCommand = ReactiveCommand.CreateFromTask(() => SetSelectedEnabledAsync(false));

        _disposables.Add(InitializeCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(RefreshCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(CreateUserCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(ChangePasswordCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(EnableSelectedCommand.IsExecuting.Subscribe(UpdateBusy));
        _disposables.Add(DisableSelectedCommand.IsExecuting.Subscribe(UpdateBusy));

        UserRoles = Enum.GetValues<UserRole>();
    }

    public ObservableCollection<UserRowViewModel> Users { get; } = [];
    public IReadOnlyList<UserRole> UserRoles { get; }

    public UserRowViewModel? SelectedUser
    {
        get => _selectedUser;
        set => this.RaiseAndSetIfChanged(ref _selectedUser, value);
    }

    public string NewUsername
    {
        get => _newUsername;
        set => this.RaiseAndSetIfChanged(ref _newUsername, value);
    }

    public UserRole NewRole
    {
        get => _newRole;
        set => this.RaiseAndSetIfChanged(ref _newRole, value);
    }

    public bool NewUserEnabled
    {
        get => _newUserEnabled;
        set => this.RaiseAndSetIfChanged(ref _newUserEnabled, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => this.RaiseAndSetIfChanged(ref _newPassword, value);
    }

    public string SelectedNewPassword
    {
        get => _selectedNewPassword;
        set => this.RaiseAndSetIfChanged(ref _selectedNewPassword, value);
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

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> InitializeCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateUserCommand { get; }
    public ReactiveCommand<Unit, Unit> ChangePasswordCommand { get; }
    public ReactiveCommand<Unit, Unit> EnableSelectedCommand { get; }
    public ReactiveCommand<Unit, Unit> DisableSelectedCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token, cancellationToken);
        await RefreshAsync(linked.Token);
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

    private async Task RefreshAsync()
        => await RefreshAsync(_lifetimeCancellation.Token);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await _userManagementService.ListUsersAsync(cancellationToken);
        if (!result.Succeeded)
        {
            SetError(result.ErrorMessage ?? "User list failed.");
            return;
        }

        var selectedId = SelectedUser?.Id;
        Users.Clear();
        foreach (var user in result.Users)
        {
            Users.Add(new UserRowViewModel(user));
        }

        SelectedUser = selectedId is null
            ? Users.FirstOrDefault()
            : Users.FirstOrDefault(user => user.Id == selectedId.Value) ?? Users.FirstOrDefault();
        ClearError();
        StatusMessage = $"Loaded {Users.Count} users.";
    }

    private async Task CreateUserAsync()
    {
        try
        {
            var result = await _userManagementService.CreateUserAsync(
                new CreateUserRequest(NewUsername, NewPassword, NewRole, NewUserEnabled),
                _lifetimeCancellation.Token);
            if (!result.Succeeded)
            {
                SetError(result.ErrorMessage ?? "User create failed.");
                return;
            }

            NewUsername = string.Empty;
            NewRole = UserRole.User;
            NewUserEnabled = true;
            await RefreshAsync(_lifetimeCancellation.Token);
            SelectedUser = Users.FirstOrDefault(user => user.Id == result.User!.Id) ?? SelectedUser;
            ClearError();
            StatusMessage = $"User created: {result.User!.Username}.";
        }
        finally
        {
            NewPassword = string.Empty;
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (SelectedUser is null)
        {
            SetError("Select a user first.");
            return;
        }

        try
        {
            var result = await _userManagementService.ChangePasswordAsync(
                new ChangePasswordRequest(SelectedUser.Id, SelectedNewPassword, SelectedUser.RowVersion),
                _lifetimeCancellation.Token);
            if (!result.Succeeded)
            {
                SetError(ToUserMessage(result));
                return;
            }

            await RefreshAsync(_lifetimeCancellation.Token);
            SelectedUser = Users.FirstOrDefault(user => user.Id == result.User!.Id) ?? SelectedUser;
            ClearError();
            StatusMessage = $"Password changed for {result.User!.Username}.";
        }
        finally
        {
            SelectedNewPassword = string.Empty;
        }
    }

    private async Task SetSelectedEnabledAsync(bool isEnabled)
    {
        if (SelectedUser is null)
        {
            SetError("Select a user first.");
            return;
        }

        var result = await _userManagementService.SetUserEnabledAsync(
            new SetUserEnabledRequest(SelectedUser.Id, isEnabled, SelectedUser.RowVersion),
            _lifetimeCancellation.Token);
        if (!result.Succeeded)
        {
            SetError(ToUserMessage(result));
            return;
        }

        await RefreshAsync(_lifetimeCancellation.Token);
        SelectedUser = Users.FirstOrDefault(user => user.Id == result.User!.Id) ?? SelectedUser;
        ClearError();
        StatusMessage = isEnabled
            ? $"User enabled: {result.User!.Username}."
            : $"User disabled: {result.User!.Username}.";
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = null;
    }

    private void ClearError() => ErrorMessage = null;

    private void UpdateBusy(bool value) => IsBusy = value;

    private static string ToUserMessage(UserManagementResult result)
        => result.ErrorCode == "UserRowVersionConflict"
            ? "User changed elsewhere. Refresh and retry."
            : result.ErrorMessage ?? "User operation failed.";
}
