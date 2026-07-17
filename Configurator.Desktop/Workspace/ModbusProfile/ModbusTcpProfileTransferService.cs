using System.Text.Json;
using System.Text.Json.Serialization;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Profiles;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Microsoft.Extensions.Options;

namespace Configurator.Desktop.Workspace.ModbusProfile;

/// <summary>
/// Сериализует, проверяет и применяет общий профиль Modbus TCP.
/// </summary>
public sealed class ModbusTcpProfileTransferService : IModbusTcpProfileTransferService
{
    private const int MaxCoils = 2000;
    private const int MaxRegisters = 123;
    private const int MinPollIntervalMs = 100;
    private const int MaxPollIntervalMs = 60000;
    private const int MinWriteConfirmationTimeoutMs = 100;
    private const int MaxWriteConfirmationTimeoutMs = 60000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAppConfigService _appConfigService;
    private readonly IOptionsMonitor<ModbusOptions> _mapOptions;
    private readonly IModbusDemoOptionsProvider _runtimeOptions;
    private readonly IModbusDataMapValidator _dataMapValidator;
    private readonly IModbusAlarmMapValidator _alarmMapValidator;
    private readonly IModbusDataMapRuntime _dataMapRuntime;

    public ModbusTcpProfileTransferService(
        IAppConfigService appConfigService,
        IOptionsMonitor<ModbusOptions> mapOptions,
        IModbusDemoOptionsProvider runtimeOptions,
        IModbusDataMapValidator dataMapValidator,
        IModbusAlarmMapValidator alarmMapValidator,
        IModbusDataMapRuntime dataMapRuntime)
    {
        _appConfigService = appConfigService;
        _mapOptions = mapOptions;
        _runtimeOptions = runtimeOptions;
        _dataMapValidator = dataMapValidator;
        _alarmMapValidator = alarmMapValidator;
        _dataMapRuntime = dataMapRuntime;
    }

    public event EventHandler? ProfileApplied;

    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var profile = ModbusTcpProfile.Create(_runtimeOptions.CurrentValue, _mapOptions.CurrentValue);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }

    public async Task<ModbusTcpProfileValidation> ReadAndValidateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ModbusTcpProfileValidation.Failure("Не выбран файл профиля Modbus TCP.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var profile = JsonSerializer.Deserialize<ModbusTcpProfile>(json, JsonOptions);
            if (profile is null)
            {
                return ModbusTcpProfileValidation.Failure("Файл не содержит профиль Modbus TCP.");
            }

            return Validate(profile);
        }
        catch (JsonException ex)
        {
            return ModbusTcpProfileValidation.Failure($"Некорректный JSON профиля Modbus TCP: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ModbusTcpProfileValidation.Failure($"Не удалось прочитать профиль Modbus TCP: {ex.Message}");
        }
    }

    public async Task<ModbusTcpProfileApplyResult> ApplyAsync(
        ModbusTcpProfile profile,
        bool applyRangeCorrection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var validation = Validate(profile);
        if (!validation.Succeeded || validation.Profile is null)
        {
            return ModbusTcpProfileApplyResult.Failure(validation.ErrorMessage ?? "Профиль Modbus TCP не прошёл проверку.");
        }

        if (validation.RangeCorrection.HasChanges && !applyRangeCorrection)
        {
            return ModbusTcpProfileApplyResult.Failure("Импорт отменён: требуется исправить диапазоны Modbus.");
        }

        var actualProfile = validation.RangeCorrection.HasChanges
            ? validation.RangeCorrection.Apply(validation.Profile)
            : validation.Profile.Clone();

        var mapOptions = _mapOptions.CurrentValue.Clone();
        // The data-map runtime reads its endpoint copy from the main Modbus
        // section. Keep it aligned with the imported runtime settings, so live
        // validation uses the same ranges shown on the Modbus TCP screen.
        mapOptions.AutostartOnWorkspaceOpen = actualProfile.Runtime.AutostartOnWorkspaceOpen;
        mapOptions.StartupMode = actualProfile.Runtime.StartupMode;
        mapOptions.WriteConfirmationTimeoutMs = actualProfile.Runtime.WriteConfirmationTimeoutMs;
        mapOptions.Client = actualProfile.Runtime.Client.Clone();
        mapOptions.Server = actualProfile.Runtime.Server.Clone();
        mapOptions.DataMap = actualProfile.DataMap.Select(point => point.Clone()).ToList();
        mapOptions.AlarmMap = actualProfile.AlarmMap.Select(alarm => alarm.Clone()).ToList();

        // Сохраняем runtime вместе с legacy DataMap, чтобы удаление demo-экрана
        // не стирало старые пользовательские настройки без явного действия.
        var runtimeOptions = actualProfile.Runtime.ApplyTo(_runtimeOptions.CurrentValue);

        try
        {
            await _appConfigService.SaveSectionsAsync(
                new Dictionary<string, object>
                {
                    [ModbusOptions.SectionName] = mapOptions,
                    [ModbusOptions.DemoSectionName] = runtimeOptions
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            return ModbusTcpProfileApplyResult.Failure($"Не удалось сохранить профиль Modbus TCP: {ex.Message}");
        }

        var runtimeApply = _dataMapRuntime.ApplyDataMap(mapOptions.DataMap);
        ProfileApplied?.Invoke(this, EventArgs.Empty);

        if (!runtimeApply.Succeeded)
        {
            return ModbusTcpProfileApplyResult.Success(
                $"Профиль сохранён, но карта не применена к работающему runtime: {OperationMessage(runtimeApply)} " +
                "Она вступит в силу после перезапуска Modbus TCP.");
        }

        return ModbusTcpProfileApplyResult.Success(
            "Профиль сохранён. Изменения endpoint вступят в силу после следующего запуска или перезапуска роли Modbus TCP.");
    }

    private ModbusTcpProfileValidation Validate(ModbusTcpProfile source)
    {
        if (!string.Equals(source.Format, ModbusTcpProfile.CurrentFormat, StringComparison.Ordinal))
        {
            return ModbusTcpProfileValidation.Failure(
                $"Неподдерживаемый формат профиля '{source.Format}'. Ожидается '{ModbusTcpProfile.CurrentFormat}'.");
        }

        if (source.Version != ModbusTcpProfile.CurrentVersion)
        {
            return ModbusTcpProfileValidation.Failure(
                $"Неподдерживаемая версия профиля {source.Version}. Ожидается {ModbusTcpProfile.CurrentVersion}.");
        }

        var profile = source.Clone();
        profile.Runtime ??= new ModbusTcpRuntimeSettings();
        profile.Runtime.Client ??= new ModbusEndpointOptions();
        profile.Runtime.Server ??= new ModbusEndpointOptions();
        profile.DataMap ??= [];
        profile.AlarmMap ??= [];

        var endpointError = ValidateRuntime(profile.Runtime);
        if (!string.IsNullOrWhiteSpace(endpointError))
        {
            return ModbusTcpProfileValidation.Failure(endpointError);
        }

        var options = new ModbusOptions
        {
            Client = profile.Runtime.Client.Clone(),
            Server = profile.Runtime.Server.Clone(),
            DataMap = profile.DataMap.Select(point => point.Clone()).ToList(),
            AlarmMap = profile.AlarmMap.Select(alarm => alarm.Clone()).ToList()
        };

        var dataValidation = _dataMapValidator.Validate(options, ModbusRunMode.None);
        if (!dataValidation.Succeeded)
        {
            return ModbusTcpProfileValidation.Failure(OperationMessage(dataValidation));
        }

        var alarmValidation = _alarmMapValidator.Validate(options, ModbusRunMode.None);
        if (!alarmValidation.Succeeded)
        {
            return ModbusTcpProfileValidation.Failure(OperationMessage(alarmValidation));
        }

        return new ModbusTcpProfileValidation
        {
            Profile = profile,
            RangeCorrection = BuildRangeCorrection(profile)
        };
    }

    private static string? ValidateRuntime(ModbusTcpRuntimeSettings runtime)
    {
        if (!Enum.IsDefined(runtime.StartupMode))
        {
            return "Профиль содержит неподдерживаемый режим автозапуска Modbus TCP.";
        }

        if (runtime.WriteConfirmationTimeoutMs is < MinWriteConfirmationTimeoutMs or > MaxWriteConfirmationTimeoutMs)
        {
            return $"Таймаут подтверждения записи должен быть от {MinWriteConfirmationTimeoutMs} до {MaxWriteConfirmationTimeoutMs} мс.";
        }

        return ValidateEndpoint("Клиент", runtime.Client, endpoint => endpoint.Host)
               ?? ValidateEndpoint("Сервер", runtime.Server, endpoint => endpoint.BindAddress);
    }

    private static string? ValidateEndpoint(
        string role,
        ModbusEndpointOptions endpoint,
        Func<ModbusEndpointOptions, string> addressSelector)
    {
        if (string.IsNullOrWhiteSpace(addressSelector(endpoint)))
        {
            return $"{role}: адрес не должен быть пустым.";
        }

        if (endpoint.Port is < 1 or > 65535)
        {
            return $"{role}: порт должен быть от 1 до 65535.";
        }

        if (endpoint.UnitId is < 1 or > 247)
        {
            return $"{role}: UnitId должен быть от 1 до 247.";
        }

        if (endpoint.PollIntervalMs is < MinPollIntervalMs or > MaxPollIntervalMs)
        {
            return $"{role}: интервал опроса должен быть от {MinPollIntervalMs} до {MaxPollIntervalMs} мс.";
        }

        if (endpoint.CoilStartAddress is < 0 or > ushort.MaxValue
            || endpoint.HoldingRegisterStartAddress is < 0 or > ushort.MaxValue)
        {
            return $"{role}: начальные адреса должны быть от 0 до {ushort.MaxValue}.";
        }

        if (endpoint.CoilCount is < 0 or > MaxCoils)
        {
            return $"{role}: количество Coils должно быть от 0 до {MaxCoils}.";
        }

        if (endpoint.RegisterCount is < 0 or > MaxRegisters)
        {
            return $"{role}: количество Holding Registers должно быть от 0 до {MaxRegisters}.";
        }

        return null;
    }

    private static ModbusRangeCorrectionProposal BuildRangeCorrection(ModbusTcpProfile profile)
    {
        var requiredCoils = 0;
        var requiredRegisters = 0;

        foreach (var point in profile.DataMap)
        {
            if (point.Area == ModbusDataArea.Coil)
            {
                requiredCoils = Math.Max(requiredCoils, point.Address + point.Length);
            }
            else
            {
                requiredRegisters = Math.Max(requiredRegisters, point.Address + point.Length);
            }
        }

        foreach (var alarm in profile.AlarmMap)
        {
            IncludeBit(alarm.Alarm);
            IncludeBit(alarm.Acknowledgement);
        }

        var client = profile.Runtime.Client;
        var server = profile.Runtime.Server;
        var clientCoils = Math.Max(client.CoilCount, requiredCoils);
        var clientRegisters = Math.Max(client.RegisterCount, requiredRegisters);
        var serverCoils = Math.Max(server.CoilCount, requiredCoils);
        var serverRegisters = Math.Max(server.RegisterCount, requiredRegisters);
        var useCoils = requiredCoils > 0;
        var useRegisters = requiredRegisters > 0;

        return new ModbusRangeCorrectionProposal
        {
            ClientCoilCount = clientCoils,
            ClientRegisterCount = clientRegisters,
            ServerCoilCount = serverCoils,
            ServerRegisterCount = serverRegisters,
            EnableClientCoils = useCoils && !client.CoilsEnabled,
            EnableClientRegisters = useRegisters && !client.HoldingRegistersEnabled,
            EnableServerCoils = useCoils && !server.CoilsEnabled,
            EnableServerRegisters = useRegisters && !server.HoldingRegistersEnabled,
            HasChanges = clientCoils != client.CoilCount
                         || clientRegisters != client.RegisterCount
                         || serverCoils != server.CoilCount
                         || serverRegisters != server.RegisterCount
                         || (useCoils && (!client.CoilsEnabled || !server.CoilsEnabled))
                         || (useRegisters && (!client.HoldingRegistersEnabled || !server.HoldingRegistersEnabled))
        };

        void IncludeBit(ModbusBitAddressOptions address)
        {
            if (address.Area == ModbusDataArea.Coil)
            {
                requiredCoils = Math.Max(requiredCoils, address.Address + 1);
            }
            else
            {
                requiredRegisters = Math.Max(requiredRegisters, address.Address + 1);
            }
        }
    }

    private static string OperationMessage(ModbusOperationResult result)
        => result.ErrorDetails is { Length: > 0 } details
            ? $"{result.ErrorMessage ?? "Ошибка проверки Modbus."} {details}"
            : result.ErrorMessage ?? "Ошибка проверки Modbus.";
}
