using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Data;

namespace Configurator.Application.Services.Modbus.Validation;

public static class ModbusResetFeedbackValidation
{
    public static string? GetError(string commandSignalId, string feedbackSignalId, ModbusOptions options)
    {
        var feedback = options.DataMap.FirstOrDefault(point =>
            string.Equals(point.Name, feedbackSignalId, StringComparison.OrdinalIgnoreCase));

        if (feedback is not { Access: ModbusDataAccess.Read, Type: ModbusValueType.Bool,
                Length: 1, Address: >= 0, WriteMode: ModbusWriteMode.Latched }
            || feedback.Area switch
            {
                ModbusDataArea.Coil => feedback.BitIndex is not null,
                ModbusDataArea.HoldingRegister => feedback.BitIndex is not (>= 0 and <= 15),
                _ => true,
            })
            return $"Для ответа ПЛК «{feedbackSignalId}» команды «{commandSignalId}» требуется отдельная точка Read/Bool.";

        if (options.DataMap.Any(point => point.IsWritable && point.Area == feedback.Area
                && feedback.Address >= point.Address && feedback.Address - point.Address < point.Length)
            || options.AlarmMap.Any(alarm => alarm.Enabled && alarm.AcknowledgementEnabled
                && alarm.Acknowledgement.Area == feedback.Area && alarm.Acknowledgement.Address == feedback.Address))
            return $"Ответ ПЛК «{feedbackSignalId}» должен находиться по адресу, доступному приложению только для чтения.";

        return null;
    }
}
