using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Dialogs;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Text;

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
        private readonly IAuthApp _authService;
        private readonly IDialogService _dialogService;
        public AuthorizationViewModel(IScreen hostScreen, IAuthApp authService, IDialogService dialogService)
        {
            // регистрируем коллбеки активации
            this.WhenActivated(disposables =>
            {
                /* код, выполняемый при активации ViewModel */
                Disposable.Create(() => { /* код при деактивации (очистка) */ })
                          .DisposeWith(disposables);
            });
            HostScreen = hostScreen;
            _authService = authService;
            _dialogService = dialogService;
        }
        // Команда входа
        [ReactiveCommand]
        private async void Authenticate()
        {
            bool result = await _dialogService.ConfirmAsync("Ваш вопрос? 123");
            System.Diagnostics.Debug.WriteLine($"Dialog result: {result}");

            if (result)
            {
                var password = await _dialogService.RequestSecretAsync("Введите пароль:");
                System.Diagnostics.Debug.WriteLine($"InputDialog result: {password}");
            }

            //// Попытка авторизации через сервис
            //if (_authService.Authenticate(_username, _password))
            //{
            //    // Успешный вход

            //}
            //else
            //{
            //    // Ошибка авторизации: можно уведомить пользователя (например, через диалог или свойство ErrorMessage)
            //    // В простом случае можно вывести MessageBox (требует Avalonia.Controls)
            //    // MessageBox.Avalonia.MessageBoxManager.GetMessageBoxStandardWindow("Ошибка", "Неверные учетные данные").Show();
            //    if (ErrorInteraction != null)
            //        ErrorInteraction.Handle("Неверные учетные данные").Subscribe();
            //}
        }
    }
}
