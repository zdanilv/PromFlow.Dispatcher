using ReactiveUI;

namespace Configurator.Desktop.Workspace;

public sealed class WorkspaceTabViewModel : ReactiveObject
{
    private readonly Func<object> _contentFactory;
    private object? _content;
    private bool _isLoaded;

    public WorkspaceTabViewModel(string id, string header, Func<object> contentFactory)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Tab id is required.", nameof(id)) : id;
        Header = string.IsNullOrWhiteSpace(header) ? throw new ArgumentException("Tab header is required.", nameof(header)) : header;
        _contentFactory = contentFactory ?? throw new ArgumentNullException(nameof(contentFactory));
    }

    public WorkspaceTabViewModel(string id, string header, object content)
        : this(id, header, () => content ?? throw new ArgumentNullException(nameof(content)))
    {
    }

    public string Id { get; }

    public string Header { get; }

    public object Content
    {
        get
        {
            EnsureContentCreated();
            return _content!;
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => this.RaiseAndSetIfChanged(ref _isLoaded, value);
    }

    public void EnsureContentCreated()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            _content = _contentFactory();
        }
        catch (Exception ex)
        {
            _content = new WorkspaceAccessUnavailableViewModel(
                "Workspace tab failed to load.",
                $"The {Header} tab could not be opened. {ex.Message}");
        }

        IsLoaded = true;
        this.RaisePropertyChanged(nameof(Content));
    }
}
