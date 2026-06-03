using Avalonia.Media;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Common;
using Configurator.Application.Services.OpcUa.Configuration;
using Configurator.Application.Services.OpcUa.Contracts;
using Configurator.Application.Services.OpcUa.Discovery;
using Configurator.Application.Services.OpcUa.Runtime;
using Configurator.Application.Services.OpcUa.Security;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.OpcUa.Validation;
using ReactiveUI;
using System;
using System.Globalization;
using System.Reactive;
using System.Threading.Tasks;

namespace Configurator.Desktop.Workspace.OpcUa;

/// <summary>
/// Строка OPC UA тега в UI: хранит конфигурацию, последнее значение и команду записи.
/// </summary>
public sealed class OpcUaTagRow : ReactiveObject
{
    private readonly Func<OpcUaTagRow, object?, Task>? _writeAsync;
    private bool _canWrite;
    private string _errorText = string.Empty;
    private bool _hasError;
    private string _inputText = string.Empty;
    private string _status = "-";
    private string _timestampText = "-";
    private string _valueText = "-";

    /// <summary>
    /// Создает строку из простого имени и адреса для демо-сценариев.
    /// </summary>
    public OpcUaTagRow(
        string name,
        OpcUaTagAddress address,
        bool canWrite,
        Func<OpcUaTagRow, object?, Task>? writeAsync = null)
        : this(new OpcUaConfiguredTag
        {
            Name = name,
            Address = address,
            Access = canWrite ? OpcUaTagAccess.ReadWrite : OpcUaTagAccess.Read
        },
            canWrite,
            writeAsync)
    {
    }

    /// <summary>
    /// Создает строку из настроенного тега и обратного вызова записи.
    /// </summary>
    public OpcUaTagRow(
        OpcUaConfiguredTag definition,
        bool canWrite,
        Func<OpcUaTagRow, object?, Task>? writeAsync = null)
    {
        Definition = definition.Clone();
        Name = Definition.Name;
        Address = Definition.Address;
        _canWrite = canWrite;
        _writeAsync = writeAsync;
        InputText = DefaultInputText(Address.DataType);
        WriteCommand = ReactiveCommand.CreateFromTask(WriteAsync, this.WhenAnyValue(x => x.CanWrite));
    }

    /// <summary>
    /// Исходная конфигурация тега, из которой строка может собрать настройки обратно.
    /// </summary>
    public OpcUaConfiguredTag Definition { get; }

    public string Name { get; }

    public OpcUaTagAddress Address { get; }

    public string Identifier => Address.Identifier;

    public string DataType => Address.DataType ?? "Object";

    public string Access => Definition.Access.ToString();

    public string Comment => Definition.Comment;

    /// <summary>
    /// Команда записи значения из InputText в OPC UA тег.
    /// </summary>
    public ReactiveCommand<Unit, Unit> WriteCommand { get; }

    public bool CanWrite
    {
        get => _canWrite;
        set => this.RaiseAndSetIfChanged(ref _canWrite, value);
    }

    public string ValueText
    {
        get => _valueText;
        private set => this.RaiseAndSetIfChanged(ref _valueText, value);
    }

    public string InputText
    {
        get => _inputText;
        set => this.RaiseAndSetIfChanged(ref _inputText, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string TimestampText
    {
        get => _timestampText;
        private set => this.RaiseAndSetIfChanged(ref _timestampText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasError, value);
            this.RaisePropertyChanged(nameof(RowBackground));
        }
    }

    public IBrush RowBackground => HasError
        ? new SolidColorBrush(Color.FromRgb(255, 241, 240))
        : Brushes.Transparent;

    /// <summary>
    /// Применяет значение из runtime snapshot без обратной записи в OPC UA.
    /// </summary>
    public void SetFromValue(OpcUaTagValue value)
    {
        Status = value.Status;
        TimestampText = value.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        if (IsErrorStatus(value.Status))
        {
            SetError(FormatError(value));
            return;
        }

        ClearError();
        ValueText = FormatValue(value.Value);

        if (string.IsNullOrWhiteSpace(InputText) || InputText == DefaultInputText(Address.DataType))
        {
            InputText = ValueText;
        }
    }

    /// <summary>
    /// Показывает ошибку операции в строке.
    /// </summary>
    public void SetError(string message)
    {
        ErrorText = string.IsNullOrWhiteSpace(message) ? "OPC UA tag operation failed." : message.Trim();
        HasError = true;
    }

    /// <summary>
    /// Сбрасывает ошибочное состояние строки.
    /// </summary>
    public void ClearError()
    {
        ErrorText = string.Empty;
        HasError = false;
    }

    /// <summary>
    /// Возвращает копию конфигурации строки для сохранения в настройках.
    /// </summary>
    public OpcUaConfiguredTag ToConfiguredTag()
    {
        var tag = Definition.Clone();
        tag.Address = new OpcUaTagAddress(
            Address.NamespaceUri,
            Address.Identifier,
            Address.DataType,
            Address.IdentifierType);
        return tag;
    }

    private async Task WriteAsync()
    {
        // Парсинг выполняется на уровне строки, чтобы runtime получал уже типизированное CLR-значение.
        if (_writeAsync is null)
        {
            return;
        }

        if (!TryParseValue(out var parsed, out var error))
        {
            SetError(error);
            return;
        }

        ClearError();
        await _writeAsync(this, parsed);
    }

    private bool TryParseValue(out object? value, out string error)
    {
        return OpcUaDataTypeSupport.TryParse(Address.DataType, InputText, out value, out error);
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            bool boolValue => boolValue ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string DefaultInputText(string? dataType)
        => OpcUaDataTypeSupport.DefaultInitialValue(dataType);

    private static bool IsErrorStatus(string status)
        => status.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
           || status.StartsWith("Bad", StringComparison.OrdinalIgnoreCase);

    private static string FormatError(OpcUaTagValue value)
    {
        var details = value.Value?.ToString();
        return string.IsNullOrWhiteSpace(details)
            ? value.Status
            : $"{value.Status}: {details}";
    }
}
