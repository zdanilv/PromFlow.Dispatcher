# Testing And Diagnostics

Use this before PRs, commits, or handoff to another agent.

## Basic Checks

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

For documentation and solution metadata changes, the minimum is:

```powershell
rg --files -g '*.md'
dotnet build .\DesktopTemplate.slnx --no-restore
git status --short
```

If build is blocked by a running `.NET Host`, stop the app/debug session and rebuild.

## RouteMap Tests

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus Tests

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## SignalId ↔ Modbus Diagnostics

| Symptom | Check |
|---|---|
| `Not configured` | Missing `Modbus.DataMap` point where `Name == SignalId` |
| Type error | `SignalValueType` versus Modbus `Type` |
| Access error | Binding direction versus point access |
| Address conflict | Duplicate coil/bit or overlapping registers |
| UI stays in mock | `RouteMapRuntime.SignalSource` and source setting |
| User resets admin settings | Shared `%LOCALAPPDATA%\Configurator\appsettings.json` and overlay after exe-local defaults |
| All values stale/offline | `ModbusDemo` state, poll interval, `StaleAfterMs` |
| Tabs visible in user mode | `Application.WorkMode` and `WorkspaceView.IsUserMode` binding |
| Offline lock not applied | System `connection.connected`, mapper, and `AreCommandsEnabled` |
| Global fault does not color the map | `Modbus.DataMap` point with `Name=system.fault`, `Read/Bool`, good quality, and value `true` |
| `Start`/`Stop` does not disable from PLC | `StartOffFeedback`/`StopOffFeedback`, `Read`, `Bool`, value `true` |
| `Start` and `Stop` both checked | `StartCommand`/`StopCommand` readback; on conflict UI should show only `Stop` checked |
| Card parameter is missing from mapping | Parameter was added on `Карточки`, role is `EquipmentParameter`, `SignalId` is not empty, and the RouteMap definition was applied |
| `Н` does not send a value | Parameter direction is `Write`/`ReadWrite`, value type parses, and a compatible `Modbus.DataMap` point exists |
| `Н` shows `SignalId ... не настроен` | Create/save the parameter row in `SignalId ↔ Modbus`; Bool uses a switch, but mapping is still required for Modbus |
| Bit write rejected | Missing first raw holding-register snapshot |
| No readback | Access, PLC echo, `WriteConfirmationTimeoutMs` |
| Wrong physical address | Start address and PLC 0/1-based notation |

## Alarm Manager Diagnostics

| Symptom | Check |
|---|---|
| Dialog does not appear | Open Workspace in `admin` or `user`, active runtime snapshot, and `Modbus.AlarmMap[].Enabled` |
| Dialog repeats too often | The alarm row `RepeatIntervalMs` |
| `Хорошо` does not acknowledge | Separate `Acknowledgement` address/bit and `AcknowledgementPulseDurationMs` |
| Address error in manager | Alarm/Acknowledgement ranges against `ModbusDemo.Client/Server` |
| RouteMap sees an alarm as SignalId | Alarm was added to `Modbus.DataMap` instead of `Modbus.AlarmMap` |
| Notification row does not close with `X` | The current alarm bit is still `true`; removal is allowed only after `Alarm=false` |
| `Очистить список` leaves rows visible | Those alarms are still active; bulk clearing uses the same check as `X` |
| Unread marker does not clear | Press `Хорошо` in the alarm dialog or from the notification row |
| `История` has no SignalId rows | Missing `Modbus.DataMap` point, bad/stale value, or unchanged value since the last quality snapshot |
| `История` address looks like `HoldingRegister 3` | That format is obsolete; the current `Адрес` column shows only offset `3` |
| Right panel does not expand on `История` | `NotificationsPanelView` must not have fixed `Width/MaxWidth=400` |
| `История` is not exported to DB | Only `NoopSessionJournalExporter` exists; DB schema and connection are not implemented |

When checking the `Менеджер тревог` table, read the column groups as follows:
`Alarm area/Offset/Bit` is the input bit that opens the dialog, `OK area/Offset/Bit` is
the separate acknowledgement bit, and `Alarm client/server` plus `OK client/server` are
physical addresses based on `ModbusDemo.Client/Server`. `Repeat ms` must be
`1000..86400000`, `Pulse ms` must be `1..60000`; `HoldingRegister` requires
`Bit=0..15`, while `Coil` must not define a bit index.

## Production Checklist

1. Verify RouteMap in `Mock`.
2. Freeze the full SignalId list.
3. Obtain approved PLC coil/register/bit layout.
4. Confirm address notation.
5. Configure endpoint and start addresses in `Modbus Demo`.
6. Verify `RouteMapRuntime`, `Modbus`, and `ModbusDemo` are saved in shared `%LOCALAPPDATA%\Configurator\appsettings.json`.
7. Fill `Modbus.DataMap` in `SignalId ↔ Modbus`.
8. Fill `Modbus.AlarmMap` in `Менеджер тревог`, without duplicating alarms in `DataMap`.
9. Clear all unconfigured/error rows.
10. Enable read-only signals first.
11. Verify quality, stale, and reconnect.
12. Verify `connection.connected=false`: commands disabled, nodes/segments offline, and cards show `Не в сети`.
13. Verify active nodes, lines, and fragments.
14. Verify modes, emergency, loader, and target.
15. Verify fault/confirmation/message dialogs and the acknowledgement pulse.
16. Verify `Уведомления`: row appears after the dialog, `Хорошо` clears unread, and `X` plus `Очистить список` remove only after `Alarm=false`.
17. Verify `История`: the panel expands, commands/received SignalIds/alarms appear with direction arrows, combined `Роли / объекты`, and numeric offset address.
18. Verify `system.fault=true`: RouteMap enters the global fault visual state, then returns to per-object logic at `false`.
19. Enable equipment commands one by one; verify `Start`/`Stop` mutual exclusion and `StartOffFeedback`/`StopOffFeedback`.
20. Verify card parameters: `Н` button, Bool switch, numeric validation, read-only rows, `Write`/`ReadWrite` save behavior, and automatic `EquipmentParameter` rows in `SignalId ↔ Modbus`.
21. Verify latched/pulse behavior, timeout, and connection loss during write.
22. Confirm interlocks and safety remain in PLC.

