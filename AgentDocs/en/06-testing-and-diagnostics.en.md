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

### Bucket card and retired TopBar mode regression matrix

Use mocks, headless UI, and local Modbus loopback only; these checks do not send
commands to a real PLC.

| Scenario | Expected result | Tests |
|---|---|---|
| Power Good false/true; Bad/Stale/offline; input/snapshot | Red/green/neutral PowerStandby; zero writes | `RouteMapRuntimeMapperTests`, `EquipmentCardViewModelTests`, UI |
| Start click/double click/timeout/cancellation | One bounded `true→false` Pulse; Stop unchanged; error visible | Unit, `RouteMapModbusAdapterTests`, loopback |
| Stop checked/unchecked/external readback | Stop-only Latched true/false; readback causes no write | Unit and loopback |
| user/admin TopBar and settings | Auto/Manual hidden with no reserved gap; Reset/Emergency available | `RouteMapSettingsDialogVisualTests` |
| v16 and older JSON, repeated migration/save | schema v17, historical fields inert, other settings preserved | `RouteMapConfigurationTests` |
| saved DataMap and old Modbus profile | Exact obsolete names removed with backup or import rejected; unrelated points retained | `RouteMapSignalRuntimeAndMappingTests`, `AppConfigServiceTests`, profile tests |

Stop both Modbus roles before saved-map cleanup. A conflicting active binding
using an obsolete exact name must block cleanup. A cleaned map takes effect at
the next role start. A second writer to the same holding register remains a
PLC contract limitation, outside what the UI tests can prove.

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
| `Start` stays checked | Bucket Start must be a momentary Button; Modbus Pulse performs the reset |
| `Stop` is wrong | Check good/fresh `StopCommand` readback and Latched true/false writes |
| PowerStandby is gray | Check `equip.bucket.power`, `Read/Bool`, good/fresh quality and connection |
| Retired mode mappings remain | Stop roles, save SignalId mapping, inspect exact IDs and backup; check active reuse conflicts |
| Card parameter is missing from mapping | Parameter was added on `Карточки`, role is `EquipmentParameter`, `SignalId` is not empty, and the RouteMap definition was applied |
| `Н` does not send a value online | Parameter direction is `Write`/`ReadWrite`, value type parses, and a compatible `Modbus.DataMap` point exists; offline save does not require mapping |
| `Н` shows `SignalId ... не настроен` | Create/save the parameter row in `SignalId ↔ Modbus`; Bool uses a switch, but mapping is still required for Modbus |
| `Н` is unavailable or follows a register | Neither card `Enabled` nor `connection.connected=false` may disable it in either mode |
| Offline setpoint was not sent | Check `route-map.json`: `pendingAutoDispatch=true` before the first Modbus reconnect; after the attempt pending clears and `lastDispatchError` keeps a failure |
| `WORD`/`DWORD`/`DATE` does not write | Check compatibility `Word → Word`, legacy `UInt16 → UInt16`, `Dword → Dword`, `Date → Date`, register-point length, and writable access |
| `String` from `Н` does not write | Increase row `Length` in `SignalId ↔ Modbus`; capacity is `Length * 2` UTF-8 bytes |
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
5. Configure endpoint and start addresses in `Modbus TCP`.
6. Verify `RouteMapRuntime`, `Modbus`, and `ModbusDemo` are saved in shared `%LOCALAPPDATA%\Configurator\appsettings.json`.
7. Fill `Modbus.DataMap` in `SignalId ↔ Modbus`.
8. Fill `Modbus.AlarmMap` in `Менеджер тревог`, without duplicating alarms in `DataMap`.
9. Clear all unconfigured/error rows.
10. Enable read-only signals first.
11. Verify quality, stale, and reconnect.
12. Verify `connection.connected=false`: commands disabled, nodes/segments offline, and cards show `Не в сети`; `Н` remains available for local save in both modes.
13. Verify active nodes, lines, and fragments.
14. Verify hidden Auto/Manual controls, Reset/Emergency, loader, and target.
15. Verify fault/confirmation/message dialogs and the acknowledgement pulse.
16. Verify `Уведомления`: row appears after the dialog, `Хорошо` clears unread, and `X` plus `Очистить список` remove only after `Alarm=false`.
17. Verify `История`: the panel expands, commands/received SignalIds/alarms appear with direction arrows, combined `Роли / объекты`, and numeric offset address.
18. Verify `system.fault=true`: RouteMap enters the global fault visual state, then returns to per-object logic at `false`.
19. Enable equipment commands one by one; verify Start Pulse and Stop Latched without cross-writes, plus `StartOffFeedback`/`StopOffFeedback`.
20. Verify card parameters: `Н` stays enabled offline in both modes regardless of `Enabled`; verify local `Write`/`ReadWrite` save without DataMap/dispatcher, restart restore, one pending dispatch after reconnect, saved failure without auto-retry, and manual retry. Also verify the Bool switch, `WORD`/`DWORD`/`DATE` validation, read-only rows, hidden `SignalId • Type` in user mode, and automatic `EquipmentParameter` rows in `SignalId ↔ Modbus`.
21. Verify latched/pulse behavior, timeout, and connection loss during write.
22. Confirm interlocks and safety remain in PLC.

