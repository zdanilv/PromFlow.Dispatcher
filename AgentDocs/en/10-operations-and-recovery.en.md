# Operations And Recovery

This guide collects Stage 14 production acceptance practices. It is not an installer or
deployment automation guide; it identifies checks operators and maintainers can run
before production handoff.

## Startup And Shutdown

`ApplicationRuntimeCoordinator` owns startup. `DesktopShutdownCoordinator` owns shutdown.
Do not start `IArchiveRuntime`, `IModbusArchiveCollector` or Modbus runtime from a
workspace view model. Use runtime state and archive health to diagnose startup failures.

Suggested lifecycle configuration values are deployment-specific. Keep timeouts short
enough for deterministic shutdown and long enough for SQLite flush/Modbus disconnect:

```json
{
  "Runtime": {
    "StartupTimeoutSeconds": 20,
    "ShutdownTimeoutSeconds": 10,
    "SingleInstanceLockTimeoutSeconds": 2
  }
}
```

## Recovery Checklist

1. If no users exist, complete administrator bootstrap locally.
2. If a license is missing or expired, sign in as Administrator and use the License tab.
3. Export an installation request when the license must be bound to this installation.
4. Install the signed license and confirm feature list and validity dates.
5. Confirm Archive health is healthy or degraded with a known cause.
6. Confirm RouteMap readback before enabling equipment commands.
7. Confirm command audit records terminal success/failure for connection loss and pulse shutdown cases.

## Manual 24-Hour Runbook

The automated Stage 14 suite uses accelerated deterministic runs. For a manual endurance
run, enable archive storage, keep Modbus connected to a representative test PLC or
simulator, and record `ArchiveHealth` at least hourly. Capture queue depth, dropped
telemetry count, last success timestamp, license state and command audit samples.
At the end, run bounded archive queries and one bounded export; do not query full history.

## Acceptance Evidence

The maintained evidence file is
`AgentDocs/implementation/STAGE14-ACCEPTANCE.md`. It records automated scenarios,
verification commands, known limitations and the status that Stage 15 has not started.

## Visual Studio Avalonia Preview Recovery

If Visual Studio shows an error such as
`AvaloniaUI.VisualStudio.Extension.Views.AvaloniaEditorWithPreview` or says that
`/AvaloniaUI.VisualStudio.Extension;component/views/avaloniaeditorwithpreview.axaml`
cannot be found, treat it as a Visual Studio extension/cache problem. That URI belongs
to the Avalonia Visual Studio extension, not to a PromFlow Dispatcher view.

Use this recovery sequence:

1. Close every Visual Studio window.
2. Update or reinstall the Avalonia for Visual Studio extension.
3. Delete the Visual Studio component cache:
   `%LOCALAPPDATA%\Microsoft\VisualStudio\*\ComponentModelCache`.
4. Reopen `DesktopTemplate.slnx`.
5. If the designer still fails, validate project XAML with the command line:
   `dotnet build .\DesktopTemplate.slnx --no-restore` and the headless UI tests.

The desktop application does not require the Visual Studio designer to run. A passing
build plus passing headless UI tests is the repository-level signal that the project
views compile and render.
