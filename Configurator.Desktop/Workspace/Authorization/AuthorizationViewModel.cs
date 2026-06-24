using Configurator.Application.Services.Authorization;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.Authorization
{
    public partial class AuthorizationViewModel : ViewModelBase, IRoutableViewModel, IActivatableViewModel
    {
        public string UrlPathSegment => "login";            // Идентификатор экрана (можно любое уникальное имя)
        public IScreen HostScreen { get; }

        public Interaction<string, Unit>? ErrorInteraction { get; } = new();
        public ViewModelActivator Activator { get; } = new ViewModelActivator();
        // Входные данные
        [Reactive] private string _username = string.Empty;
        [Reactive] private string _password = string.Empty;
        private readonly IAuthenticationService _authenticationService;
        public AuthorizationViewModel(IScreen hostScreen, IAuthenticationService authenticationService)
        {
            // регистрируем коллбеки активации
            this.WhenActivated(disposables =>
            {
                /* код, выполняемый при активации ViewModel */
                Disposable.Create(() => { /* код при деактивации (очистка) */ })
                          .DisposeWith(disposables);
            });
            HostScreen = hostScreen;
            _authenticationService = authenticationService;
        }
        // Команда входа
        [ReactiveCommand]
        private async Task Authenticate()
        {
            var result = await _authenticationService.AuthenticateAsync(new AuthenticationRequest(_username, _password));
            if (!result.Succeeded && ErrorInteraction != null)
            {
                await ErrorInteraction.Handle(result.ErrorMessage ?? "Неверные учетные данные").FirstAsync();
            }
        }
    }
}
