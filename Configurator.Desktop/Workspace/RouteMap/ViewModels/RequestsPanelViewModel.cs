using System.Collections.ObjectModel;
using Configurator.Desktop.Workspace.RouteMap.Models;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class RequestsPanelViewModel : ViewModelBase
{
    private bool _isAutomatic;
    private bool _isManual = true;
    private bool _isQueueRunning;
    private int _nextRequestNumber;

    public RequestsPanelViewModel(
        IEnumerable<RequestItem> requests,
        IEnumerable<RequestTemplateItem> requestTemplates)
    {
        Requests = new ObservableCollection<RequestItem>(requests);
        RequestTemplates = new ObservableCollection<RequestTemplateItem>(requestTemplates);
        _nextRequestNumber = Requests.Count == 0 ? 1 : Requests.Max(x => x.Number) + 1;

        StartQueueCommand = ReactiveCommand.Create(() =>
        {
            IsQueueRunning = true;
        });
        StopQueueCommand = ReactiveCommand.Create(() =>
        {
            IsQueueRunning = false;
        });
        AddRequestCommand = ReactiveCommand.Create(AddRequest);
    }

    public ObservableCollection<RequestItem> Requests { get; }
    public ObservableCollection<RequestTemplateItem> RequestTemplates { get; }

    public bool IsAutomatic
    {
        get => _isAutomatic;
        set => this.RaiseAndSetIfChanged(ref _isAutomatic, value);
    }

    public bool IsManual
    {
        get => _isManual;
        set => this.RaiseAndSetIfChanged(ref _isManual, value);
    }

    public bool IsQueueRunning
    {
        get => _isQueueRunning;
        set
        {
            this.RaiseAndSetIfChanged(ref _isQueueRunning, value);
            this.RaisePropertyChanged(nameof(StartQueueText));
        }
    }

    public string StartQueueText => IsQueueRunning ? "ОЧЕРЕДЬ ЗАПУЩЕНА" : "ЗАПУСТИТЬ ОЧЕРЕДЬ";

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> StartQueueCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> StopQueueCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AddRequestCommand { get; }

    public void ApplyRuntime(RouteMapRuntimeState runtimeState)
    {
        IsAutomatic = runtimeState.IsAutomaticMode;
        IsManual = runtimeState.IsManualMode;
        IsQueueRunning = runtimeState.IsQueueRunning;
    }

    private void AddRequest()
    {
        var template = RequestTemplates.FirstOrDefault();
        Requests.Add(new RequestItem(
            _nextRequestNumber++,
            template?.Recipe ?? "Рецепт",
            template?.Volume ?? "1.0 м³",
            "Бункер",
            "Кюбель"));
    }
}
