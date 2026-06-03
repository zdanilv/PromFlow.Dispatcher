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
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Subjects;

namespace Configurator.Desktop.Dialogs.OpcUaTagEditorDialog;

/// <summary>
/// Модель представления диалога ручного создания или редактирования OPC UA тега.
/// </summary>
public sealed class OpcUaTagEditorDialogViewModel : ReactiveObject
{
    private readonly Subject<OpcUaConfiguredTag?> _result = new();
    private string _access;
    private string _comment = string.Empty;
    private string _dataType;
    private string _errorText = string.Empty;
    private string _identifier = string.Empty;
    private string _initialValue = string.Empty;
    private string _name = string.Empty;
    private string _namespaceUri = string.Empty;

    /// <summary>
    /// Создает редактор тега с дефолтными правами доступа для выбранного назначения.
    /// </summary>
    public OpcUaTagEditorDialogViewModel(
        string title,
        OpcUaConfiguredTag? tag,
        OpcUaImportTarget target)
    {
        Title = title;
        Target = target;
        _dataType = tag?.Address.DataType ?? "String";
        _access = (tag?.Access ?? DefaultAccess(target)).ToString();
        _name = tag?.Name ?? string.Empty;
        _namespaceUri = tag?.Address.NamespaceUri ?? "urn:Configurator:OpcUa:Demo";
        _identifier = tag?.Address.Identifier ?? string.Empty;
        _comment = tag?.Comment ?? string.Empty;
        _initialValue = tag?.InitialValue ?? DefaultInitialValue(_dataType);

        OkCommand = ReactiveCommand.Create(Submit);
        CancelCommand = ReactiveCommand.Create(() => _result.OnNext(null));
    }

    public string Title { get; }

    public OpcUaImportTarget Target { get; }

    public ObservableCollection<string> DataTypes { get; } = new(OpcUaDataTypeSupport.SupportedDataTypes);

    public ObservableCollection<string> AccessModes { get; } = ["Read", "Write", "ReadWrite"];

    public ReactiveCommand<Unit, Unit> OkCommand { get; }

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public IObservable<OpcUaConfiguredTag?> Result => _result;

    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    public string NamespaceUri
    {
        get => _namespaceUri;
        set => this.RaiseAndSetIfChanged(ref _namespaceUri, value);
    }

    public string Identifier
    {
        get => _identifier;
        set => this.RaiseAndSetIfChanged(ref _identifier, value);
    }

    public string DataType
    {
        get => _dataType;
        set
        {
            this.RaiseAndSetIfChanged(ref _dataType, value);
            if (string.IsNullOrWhiteSpace(InitialValue))
            {
                InitialValue = DefaultInitialValue(value);
            }
        }
    }

    public string Access
    {
        get => _access;
        set => this.RaiseAndSetIfChanged(ref _access, value);
    }

    public string InitialValue
    {
        get => _initialValue;
        set => this.RaiseAndSetIfChanged(ref _initialValue, value);
    }

    public string Comment
    {
        get => _comment;
        set => this.RaiseAndSetIfChanged(ref _comment, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set => this.RaiseAndSetIfChanged(ref _errorText, value);
    }

    private void Submit()
    {
        // Диалог проверяет только UI-обязательные поля; полную валидацию списков выполняет экран перед сохранением.
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorText = "Name is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NamespaceUri))
        {
            ErrorText = "Namespace URI is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Identifier))
        {
            ErrorText = "Identifier is required.";
            return;
        }

        if (!Enum.TryParse<OpcUaTagAccess>(Access, out var parsedAccess))
        {
            ErrorText = "Access mode is invalid.";
            return;
        }

        ErrorText = string.Empty;
        _result.OnNext(new OpcUaConfiguredTag
        {
            Name = Name.Trim(),
            Address = new OpcUaTagAddress(
                NamespaceUri.Trim(),
                Identifier.Trim(),
                DataType,
                "String"),
            Access = parsedAccess,
            Comment = Comment.Trim(),
            InitialValue = InitialValue
        });
    }

    private static OpcUaTagAccess DefaultAccess(OpcUaImportTarget target)
        => target == OpcUaImportTarget.ServerTelemetry
            ? OpcUaTagAccess.Read
            : OpcUaTagAccess.ReadWrite;

    private static string DefaultInitialValue(string dataType)
        => OpcUaDataTypeSupport.DefaultInitialValue(dataType);
}
