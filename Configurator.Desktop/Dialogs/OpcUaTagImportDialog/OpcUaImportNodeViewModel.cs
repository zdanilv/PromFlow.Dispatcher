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
using System.Collections.ObjectModel;

namespace Configurator.Desktop.Dialogs.OpcUaTagImportDialog;

/// <summary>
/// Узел дерева импорта OPC UA тегов с состоянием выбора для UI.
/// </summary>
public sealed class OpcUaImportNodeViewModel : ReactiveObject
{
    private readonly Action? _selectionChanged;
    private bool _isSelected;

    /// <summary>
    /// Создает UI-узел и рекурсивно оборачивает дочерние Browse-узлы.
    /// </summary>
    public OpcUaImportNodeViewModel(OpcUaBrowseNode node, Action? selectionChanged = null)
    {
        Node = node;
        _selectionChanged = selectionChanged;
        Children = new ObservableCollection<OpcUaImportNodeViewModel>(
            node.Children.Select(child => new OpcUaImportNodeViewModel(child, selectionChanged)));
    }

    public OpcUaBrowseNode Node { get; }

    public ObservableCollection<OpcUaImportNodeViewModel> Children { get; }

    public string Name => Node.Name;

    public string DataType => !string.IsNullOrWhiteSpace(Node.NormalizedDataType) && Node.NormalizedDataType != "none"
        ? Node.NormalizedDataType
        : Node.DataType;

    public string Access => Node.Access == OpcUaTagAccess.None ? string.Empty : Node.Access.ToString();

    public string Comment => Node.Comment;

    public string NodeId => Node.NodeId;

    public bool IsSelectable => Node.IsSelectable;

    public string UnsupportedReason => Node.UnsupportedReason;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsSelectable && value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _isSelected, value);
            _selectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// Возвращает выбранные поддерживаемые переменные из текущего поддерева.
    /// </summary>
    public IEnumerable<OpcUaBrowseNode> SelectedNodes()
    {
        if (IsSelected && Node.Address is not null)
        {
            yield return Node;
        }

        foreach (var child in Children)
        {
            foreach (var selected in child.SelectedNodes())
            {
                yield return selected;
            }
        }
    }
}
