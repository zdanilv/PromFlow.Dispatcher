using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;

namespace Configurator.Desktop.Dialogs.InputDialog
{
    public class InputDialogViewModel : ReactiveObject
    {
        public string Message { get; }
        private string _password = string.Empty;

        public string Password
        {
            get => _password;
            set => this.RaiseAndSetIfChanged(ref _password, value);
        }

        /// <summary>
        /// Publishes a confirmation intent with current secret value,
        /// but does not close or control the dialog window directly.
        /// </summary>
        public ReactiveCommand<Unit, string?> OkCommand { get; }

        /// <summary>
        /// Publishes a cancellation intent,
        /// but does not close or control the dialog window directly.
        /// </summary>
        public ReactiveCommand<Unit, string?> CancelCommand { get; }

        public IObservable<string?> Result { get; }

        public InputDialogViewModel(string message)
        {
            Message = message;
            OkCommand = ReactiveCommand.Create(() => Password);
            CancelCommand = ReactiveCommand.Create(() => (string?)null);

            Result = OkCommand.Merge(CancelCommand);
        }
    }
}
