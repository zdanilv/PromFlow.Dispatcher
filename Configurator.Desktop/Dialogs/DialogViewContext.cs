using Avalonia.Controls;
using System;

namespace Configurator.Desktop.Dialogs;

public sealed record DialogViewContext<TResult>(
    Control View,
    IObservable<TResult> ResultStream,
    string HostIdentifier = DialogHostIds.Root);
