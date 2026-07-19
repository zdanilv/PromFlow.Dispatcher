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
        string message,
        string? registerValueText = null)
    {
        Kind = kind;
        Message = message;
        RegisterValueText = registerValueText;
        OkCommand = ReactiveCommand.Create(() => _result.OnNext(true));
        CloseCommand = ReactiveCommand.Create(() => _result.OnNext(false));
    }

    public ModbusAlarmKind Kind { get; }
    public string Message { get; }
    public string? RegisterValueText { get; }
    public bool HasRegisterValueText => !string.IsNullOrWhiteSpace(RegisterValueText);
    public IObservable<bool> Result => _result;
    public ReactiveCommand<Unit, Unit> OkCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public bool IsFault => Kind == ModbusAlarmKind.Fault;
    public string Title => Kind switch
    {
        ModbusAlarmKind.Fault => "Авария",
        ModbusAlarmKind.Confirmation => "Повторное подтверждение",
        ModbusAlarmKind.Message => "Сообщение",
        _ => "Сообщение",
    };
    public string BadgeText => Kind switch
    {
        ModbusAlarmKind.Fault => "!",
        ModbusAlarmKind.Confirmation => "?",
        ModbusAlarmKind.Message => "i",
        _ => "i",
    };
    public IBrush HeaderBackground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#B42318")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#9A6700")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#295B8D")),
        _ => new SolidColorBrush(Color.Parse("#295B8D")),
    };
    public IBrush BadgeBackground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#FEE4E2")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#FFF4CC")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#E8F2FF")),
        _ => new SolidColorBrush(Color.Parse("#E8F2FF")),
    };
    public IBrush BadgeForeground => Kind switch
    {
        ModbusAlarmKind.Fault => new SolidColorBrush(Color.Parse("#B42318")),
        ModbusAlarmKind.Confirmation => new SolidColorBrush(Color.Parse("#8A5A00")),
        ModbusAlarmKind.Message => new SolidColorBrush(Color.Parse("#295B8D")),
        _ => new SolidColorBrush(Color.Parse("#295B8D")),
    };
}
