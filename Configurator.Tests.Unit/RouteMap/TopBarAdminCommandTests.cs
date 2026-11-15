using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using ReactiveUI;
using Xunit;
using ReactiveUnit = System.Reactive.Unit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class TopBarAdminCommandTests
{
    private static readonly RouteMapDefinition Seed = RouteMapSeed.Create();
    private static RouteTopBarSettings Settings => Seed.TopBar!;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Work_mode_guards_direct_execution_independently_of_settings_visibility(bool isAdminMode)
    {
        var dispatcher = new RecordingDispatcher();
        var settings = Settings with
        {
            Unload = Rename(Settings.Unload, "test.unload", "Unload"),
            Rotation = Rename(Settings.Rotation, "test.rotate", "Rotate"),
            Load = Rename(Settings.Load, "test.load", "Load"),
        };
        var topBar = new TopBarViewModel(isAdminMode, commandDispatcher: dispatcher, settings: settings,
            isSettingsVisible: !isAdminMode);
        var signals = Connected();
        const string unloadId = "test.unload";
        signals[unloadId] = new(unloadId, true, SignalValueType.Bool, DateTimeOffset.UtcNow, true, false);
        ApplySnapshot(topBar, settings, signals);

        Assert.Equal(isAdminMode, topBar.AreAdminCommandsVisible);
        Assert.Equal(!isAdminMode, topBar.IsSettingsVisible);
        Assert.Empty(dispatcher.Requests);
        Assert.Equal(isAdminMode, topBar.IsUnloadEnabled);
        Assert.Equal(isAdminMode, topBar.IsRotationEnabled);
        Assert.Equal(isAdminMode, topBar.IsLoadEnabled);
        foreach (var command in new[] { topBar.UnloadCommand, topBar.RotationCommand, topBar.LoadCommand })
        {
            Assert.Equal(isAdminMode, ((ICommand)command).CanExecute(null));
            await Execute(command);
        }

        if (isAdminMode)
            Assert.Collection(dispatcher.Requests,
                request => AssertPulse(request, "test.unload"),
                request => AssertPulse(request, "test.rotate"),
                request => AssertPulse(request, "test.load"));
        else
            Assert.Empty(dispatcher.Requests);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("bad")]
    [InlineData("stale")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("wrong-type")]
    public async Task Configured_enabled_requires_a_good_fresh_true(string state)
    {
        for (var index = 0; index < 3; index++)
        {
            var button = WithEnabledBinding(GetButton(index));
            var settings = ReplaceButton(index, button);
            var enabledId = button.EnabledBinding!.SignalId;
            var dispatcher = new RecordingDispatcher();
            var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: settings);
            var signals = Connected();
            if (state != "missing")
            {
                object? value = state == "null" ? null : state != "false";
                signals[enabledId] = new(enabledId, value, state == "wrong-type" ? SignalValueType.String : SignalValueType.Bool,
                    DateTimeOffset.UtcNow, IsQualityGood: state != "bad", IsStale: state == "stale");
            }

            ApplySnapshot(topBar, settings, signals);

            Assert.Empty(dispatcher.Requests);
            var commands = GetCommands(topBar);
            var allowed = state == "true";
            for (var commandIndex = 0; commandIndex < commands.Length; commandIndex++)
                Assert.Equal(commandIndex != index || allowed, ((ICommand)commands[commandIndex]).CanExecute(null));

            bool?[] enabledValues = [topBar.UnloadEnabledSignalValue, topBar.RotationEnabledSignalValue, topBar.LoadEnabledSignalValue];
            Assert.Equal(state switch { "true" => true, "false" => false, _ => (bool?)null }, enabledValues[index]);
            await Execute(commands[index]);
            if (allowed)
                AssertPulse(Assert.Single(dispatcher.Requests), button.Binding.SignalId);
            else
                Assert.Empty(dispatcher.Requests);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("blank")]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("type")]
    [InlineData("role")]
    public async Task Invalid_command_binding_blocks_dispatch(string invalid)
    {
        for (var index = 0; index < 3; index++)
        {
            var button = GetButton(index);
            var binding = button.Binding;
            var settings = ReplaceButton(index, button with
            {
                Binding = invalid switch
                {
                    "missing" => null!,
                    "blank" => binding with { SignalId = " " },
                    "read" => binding with { Direction = SignalBindingDirection.Read },
                    "write" => binding with { Direction = SignalBindingDirection.Write },
                    "type" => binding with { ValueType = SignalValueType.String },
                    "role" => binding with { Role = SignalBindingRole.ResetCommand },
                    _ => throw new ArgumentOutOfRangeException(nameof(invalid)),
                },
            });
            var dispatcher = new RecordingDispatcher();
            var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: settings);
            ApplySnapshot(topBar, settings, Connected());

            var command = GetCommands(topBar)[index];
            Assert.False(((ICommand)command).CanExecute(null));
            await Execute(command);
            Assert.Empty(dispatcher.Requests);
        }
    }

    [Theory]
    [InlineData("none")]
    [InlineData("missing-connection")]
    [InlineData("offline")]
    [InlineData("bad")]
    [InlineData("stale")]
    public async Task Connection_loss_blocks_direct_execution(string state)
    {
        var dispatcher = new RecordingDispatcher();
        var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: Settings);
        var signals = Connected();
        ApplySnapshot(topBar, Settings, signals);
        Assert.True(topBar.IsUnloadEnabled);
        var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
        signals[connectionId] = signals[connectionId] with
        {
            Value = state != "offline",
            IsQualityGood = state != "bad",
            IsStale = state == "stale",
        };
        if (state == "missing-connection")
            signals.Remove(connectionId);
        ApplySnapshot(topBar, Settings, state == "none" ? null : signals);

        Assert.False(topBar.IsUnloadEnabled);
        Assert.False(topBar.IsRotationEnabled);
        Assert.False(topBar.IsLoadEnabled);
        await Execute(topBar.UnloadCommand);
        await Execute(topBar.RotationCommand);
        await Execute(topBar.LoadCommand);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Busy_guard_is_per_command_and_apply_preserves_in_flight_binding()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originalId = Settings.Unload.Binding.SignalId;
        var dispatcher = new RecordingDispatcher((request, _) =>
            request.SignalId == originalId ? completion.Task : Task.CompletedTask);
        var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: Settings);
        var changedProperties = new List<string?>();
        topBar.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);
        var connected = Connected();
        ApplySnapshot(topBar, Settings, connected);

        var inFlight = Execute(topBar.UnloadCommand);
        Assert.True(topBar.IsUnloadSending);
        Assert.False(topBar.IsUnloadEnabled);
        Assert.False(((ICommand)topBar.UnloadCommand).CanExecute(null));
        Assert.True(topBar.IsRotationEnabled);
        Assert.True(topBar.IsLoadEnabled);
        Assert.False(topBar.IsRotationSending);
        Assert.False(topBar.IsLoadSending);
        AssertPulse(Assert.Single(dispatcher.Requests), originalId);
        await Execute(topBar.UnloadCommand);
        Assert.Single(dispatcher.Requests);
        await Execute(topBar.RotationCommand);

        var settings = Settings with
        {
            Unload = WithEnabledBinding(Rename(Settings.Unload, "test.unload.next", "Unload next")),
            Rotation = Settings.Rotation with { Text = "Rotation next" },
            Load = Settings.Load with { Text = "Load next" },
        };
        topBar.ApplySettings(settings);
        ApplySnapshot(topBar, settings, connected);
        Assert.True(topBar.IsUnloadSending);
        Assert.Equal("Unload next", topBar.UnloadText);
        Assert.Equal("Rotation next", topBar.RotationText);
        Assert.Equal("Load next", topBar.LoadText);
        Assert.Contains(nameof(topBar.UnloadText), changedProperties);
        Assert.Contains(nameof(topBar.RotationText), changedProperties);
        Assert.Contains(nameof(topBar.LoadText), changedProperties);
        await Execute(topBar.UnloadCommand);
        Assert.Equal(2, dispatcher.Requests.Count);

        completion.SetResult();
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(topBar.IsUnloadSending);
        Assert.False(topBar.IsUnloadEnabled);
        await Execute(topBar.UnloadCommand);
        Assert.Equal(2, dispatcher.Requests.Count);

        settings = settings with { Unload = settings.Unload with { EnabledBinding = null } };
        topBar.ApplySettings(settings);
        ApplySnapshot(topBar, settings, connected);
        Assert.True(topBar.IsUnloadEnabled);
        Assert.Equal(2, dispatcher.Requests.Count);
        await Execute(topBar.UnloadCommand);
        Assert.Collection(dispatcher.Requests,
            request => AssertPulse(request, originalId),
            request => AssertPulse(request, Settings.Rotation.Binding.SignalId),
            request => AssertPulse(request, "test.unload.next"));
    }

    [Theory]
    [InlineData("error", "Write failed")]
    [InlineData("timeout", "Истекло время")]
    [InlineData("canceled", "отменена")]
    public async Task Failed_dispatch_releases_busy_and_reports_outcome_without_retry(string failure, string expectedError)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatcher = new RecordingDispatcher((_, _) => completion.Task);
        var errors = new List<string>();
        var successCount = 0;
        var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: Settings,
            commandErrorHandler: errors.Add, commandSuccessHandler: () => successCount++);
        ApplySnapshot(topBar, Settings, Connected());

        var inFlight = Execute(topBar.LoadCommand);
        Assert.True(topBar.IsLoadSending);
        if (failure == "canceled")
            completion.SetCanceled();
        else
            completion.SetException(failure == "timeout" ? new TimeoutException("Transport timeout") : new InvalidOperationException("Write failed"));
        await inFlight.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(topBar.IsLoadSending);
        Assert.True(topBar.IsLoadEnabled);
        Assert.Equal(0, successCount);
        Assert.Contains(expectedError, Assert.Single(errors));
        AssertPulse(Assert.Single(dispatcher.Requests), Settings.Load.Binding.SignalId);
    }

    [Fact]
    public async Task Disposing_direct_execution_forwards_cancellation_and_waits_for_dispatcher_before_clearing_busy()
    {
        var dispatcherCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedToken = CancellationToken.None;
        var dispatcher = new RecordingDispatcher((_, token) =>
        {
            receivedToken = token;
            return dispatcherCompletion.Task;
        });
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var topBar = new TopBarViewModel(true, commandDispatcher: dispatcher, settings: Settings);
        ApplySnapshot(topBar, Settings, Connected());
        topBar.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(topBar.IsRotationSending) && !topBar.IsRotationSending)
                cleared.TrySetResult();
        };

        using var execution = topBar.RotationCommand.Execute().Subscribe();
        Assert.True(topBar.IsRotationSending);
        execution.Dispose();
        Assert.True(receivedToken.IsCancellationRequested);
        Assert.True(topBar.IsRotationSending);
        await Execute(topBar.RotationCommand);
        dispatcherCompletion.SetCanceled(receivedToken);
        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(topBar.IsRotationEnabled);
        Assert.Single(dispatcher.Requests);
    }

    private static Task<ReactiveUnit> Execute(ReactiveCommand<ReactiveUnit, ReactiveUnit> command) => command.Execute().ToTask();

    private static RouteTopBarButtonSettings GetButton(int index) =>
        new[] { Settings.Unload, Settings.Rotation, Settings.Load }[index];

    private static RouteTopBarSettings ReplaceButton(int index, RouteTopBarButtonSettings button) => index switch
    {
        0 => Settings with { Unload = button },
        1 => Settings with { Rotation = button },
        2 => Settings with { Load = button },
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private static ReactiveCommand<ReactiveUnit, ReactiveUnit>[] GetCommands(TopBarViewModel topBar) =>
        [topBar.UnloadCommand, topBar.RotationCommand, topBar.LoadCommand];

    private static void ApplySnapshot(TopBarViewModel topBar, RouteTopBarSettings settings, Dictionary<string, SignalValue>? signals) =>
        topBar.ApplyAdminCommandRuntime(new RouteMapRuntimeMapper(Seed with { TopBar = settings }).Map(signals));

    private static Dictionary<string, SignalValue> Connected()
    {
        var id = RouteMapSystemSignalIds.ConnectionConnected;
        return new Dictionary<string, SignalValue> { [id] = new(id, true, SignalValueType.Bool, DateTimeOffset.UtcNow, true, false) };
    }

    private static RouteTopBarButtonSettings Rename(RouteTopBarButtonSettings settings, string id, string text) =>
        settings with { Text = text, Binding = settings.Binding with { SignalId = id } };

    private static RouteTopBarButtonSettings WithEnabledBinding(RouteTopBarButtonSettings settings) => settings with
    {
        EnabledBinding = new(SignalBindingRole.Enabled, $"{settings.Binding.SignalId}.enabled", SignalBindingDirection.Read, SignalValueType.Bool),
    };

    private static void AssertPulse(SignalWriteRequest request, string id)
    {
        Assert.Equal(id, request.SignalId);
        Assert.Equal(true, request.Value);
        Assert.Equal(SignalValueType.Bool, request.ValueType);
        Assert.Equal(ModbusWriteMode.Pulse, request.RequiredWriteMode);
    }

    private sealed class RecordingDispatcher(Func<SignalWriteRequest, CancellationToken, Task>? dispatch = null) : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return dispatch?.Invoke(request, cancellationToken) ?? Task.CompletedTask;
        }
    }
}
