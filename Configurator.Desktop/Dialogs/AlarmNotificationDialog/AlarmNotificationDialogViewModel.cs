using System.Reactive;
using System.Reactive.Subjects;
using Avalonia.Media;
using Configurator.Application.Services.Modbus.Configuration;
using ReactiveUI;

namespace Configurator.Desktop.Dialogs.AlarmNotificationDialog;

public sealed class AlarmNotificationDialogViewModel : ReactiveObject
{
    private readonly Subject<bool> _result = new();

    public AlarmNotificationDialogViewModel(
        ModbusAlarmKind kind,
        string message)
    {
        Kind = kind;
        Message = message;
        OkCommand = ReactiveCommand.Create(() => _result.OnNext(true));
        CloseCommand = ReactiveCommand.Create(() => _result.OnNext(false));
    }

    public ModbusAlarmKind Kind { get; }
    public string Message { get; }
    public IObservable<bool> Result => _result;
    public ReactiveCommand<Unit, Unit> OkCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public bool IsFault => Kind == ModbusAlarmKind.Fault;
    public string Title => IsFault ? "Авария" : "Повторное подтверждение";
    public string BadgeText => IsFault ? "!" : "?";
    public IBrush HeaderBackground => IsFault
        ? new SolidColorBrush(Color.Parse("#B42318"))
        : new SolidColorBrush(Color.Parse("#9A6700"));
    public IBrush BadgeBackground => IsFault
        ? new SolidColorBrush(Color.Parse("#FEE4E2"))
        : new SolidColorBrush(Color.Parse("#FFF4CC"));
    public IBrush BadgeForeground => IsFault
        ? new SolidColorBrush(Color.Parse("#B42318"))
        : new SolidColorBrush(Color.Parse("#8A5A00"));
}
