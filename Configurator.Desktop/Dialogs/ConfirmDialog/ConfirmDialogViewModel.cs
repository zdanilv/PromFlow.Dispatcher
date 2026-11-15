using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;

namespace Configurator.Desktop.Dialogs.ConfirmDialog
{
    public class ConfirmDialogViewModel : ReactiveObject
    {
        public string Message { get; }
        public string Title { get; }
        public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
        public string ConfirmText { get; }
        public string CancelText { get; }

        /// <summary>
        /// Publishes a confirmation intent from the user (agree action),
        /// but does not close or control the dialog window directly.
        /// </summary>
        public ReactiveCommand<Unit, bool> OkCommand { get; }

        /// <summary>
        /// Publishes a cancellation intent from the user (reject/close action),
        /// but does not close or control the dialog window directly.
        /// </summary>
        public ReactiveCommand<Unit, bool> CancelCommand { get; }

        public IObservable<bool> Result { get; }

        public ConfirmDialogViewModel(string message)
            : this(message, string.Empty, "ОК", "Закрыть")
        {
        }

        public ConfirmDialogViewModel(string message, string title, string confirmText, string cancelText)
        {
            Message = message;
            Title = title;
            ConfirmText = confirmText;
            CancelText = cancelText;
            OkCommand = ReactiveCommand.Create(() => true);
            CancelCommand = ReactiveCommand.Create(() => false);

            Result = OkCommand.Merge(CancelCommand);
        }
    }
}
