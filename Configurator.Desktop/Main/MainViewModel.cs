using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Dialogs;
using Configurator.Desktop.Workspace;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System;
using System.Reactive.Linq;

namespace Configurator.Desktop.Main;

public partial class MainViewModel : ViewModelBase, IScreen
{
    private readonly IAuthApp _authService;
    private readonly IDialogService _dialogService;
    private readonly Func<IScreen, WorkspaceViewModel> _workspaceFactory;

    public RoutingState Router { get; } = new RoutingState();

    [Reactive] private string? _label = "Test VM";

    public MainViewModel(
        IAuthApp authService,
        IDialogService dialogService,
        Func<IScreen, WorkspaceViewModel> workspaceFactory)
    {
        _authService = authService;
        _dialogService = dialogService;
        _workspaceFactory = workspaceFactory;

        // При запуске приложения навигируем на экран логина
        var workVm = _workspaceFactory(this);
        Router.Navigate.Execute(workVm).Subscribe();

        //var authVm = new AuthorizationViewModel(this, _authService, _dialogService);
        //Router.Navigate.Execute(authVm).Subscribe();

        //_authService
        //    .WhenAnyValue(x => x.IsAuthenticated)
        //    .Where(isAuth => isAuth)
        //    .Subscribe(isAuth =>
        //    {
        //        var workVm = _workspaceFactory(this);
        //        Router.Navigate.Execute(workVm);
        //    });
    }
}
