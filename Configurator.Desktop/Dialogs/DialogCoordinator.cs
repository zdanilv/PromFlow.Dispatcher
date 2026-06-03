using DialogHostAvalonia;
using System.Reactive.Linq;

namespace Configurator.Desktop.Dialogs;

public sealed class DialogCoordinator
{
    /// <summary>
    /// Shows prepared dialog view in the root host and bridges first emitted view-model result to host closing payload.
    /// </summary>
    /// <typeparam name="TResult">Type of dialog result emitted by the view-model.</typeparam>
    /// <param name="context">Prepared dialog context that contains view and one-shot result stream.</param>
    /// <param name="ct">Cancellation token for pre/post show cancellation checks.</param>
    /// <returns>Result provided by the dialog interaction or host close payload.</returns>
    public async Task<TResult?> ShowAsync<TResult>(DialogViewContext<TResult> context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var _ = context.ResultStream.Take(1)
            .Subscribe(result => DialogHost.Close(DialogHostIds.Root, result));
        var result = await DialogHost.Show(context.View, DialogHostIds.Root);

        ct.ThrowIfCancellationRequested();
        return result is TResult typed ? typed : default;
    }
}
