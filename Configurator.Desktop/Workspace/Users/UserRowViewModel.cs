using Configurator.Application.Services.Authorization;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.Users;

public sealed class UserRowViewModel : ReactiveObject
{
    private UserSummary _summary;

    public UserRowViewModel(UserSummary summary)
    {
        _summary = summary;
    }

    public Guid Id => _summary.Id;
    public string Username => _summary.Username;
    public UserRole Role => _summary.Role;
    public bool IsEnabled => _summary.IsEnabled;
    public int FailedLoginCount => _summary.FailedLoginCount;
    public string LockoutUntilUtc => FormatDate(_summary.LockoutUntilUtc);
    public string LastLoginAtUtc => FormatDate(_summary.LastLoginAtUtc);
    public string UpdatedAtUtc => FormatDate(_summary.UpdatedAtUtc);
    public long RowVersion => _summary.RowVersion;

    public void Update(UserSummary summary)
    {
        _summary = summary;
        this.RaisePropertyChanged(nameof(Id));
        this.RaisePropertyChanged(nameof(Username));
        this.RaisePropertyChanged(nameof(Role));
        this.RaisePropertyChanged(nameof(IsEnabled));
        this.RaisePropertyChanged(nameof(FailedLoginCount));
        this.RaisePropertyChanged(nameof(LockoutUntilUtc));
        this.RaisePropertyChanged(nameof(LastLoginAtUtc));
        this.RaisePropertyChanged(nameof(UpdatedAtUtc));
        this.RaisePropertyChanged(nameof(RowVersion));
    }

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToUniversalTime().ToString("u") ?? string.Empty;
}
