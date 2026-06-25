# Правила разработки

Следуйте этим правилам при изменении RouteMap, Modbus TCP, SignalId mapping, archive,
authorization, licensing или lifecycle code.

## Architecture

- Не помещайте Modbus addresses в XAML, ViewModels, `RouteMapControl` или `route-map.json`.
- Физическая PLC mapping живет в `Modbus.DataMap`.
- Не смешивайте `Modbus.DataMap` и `ModbusDemo.DataMap`.
- TCP endpoint settings остаются в `ModbusDemo`; startup/shutdown остаются в centralized lifecycle.
- Не регистрируйте `RouteMapDefinition` как immutable singleton.
- Не обновляйте Avalonia UI напрямую из Modbus callbacks.
- Interlocks, safety и final actuator permissions остаются в PLC logic.

## Authorization и license

- UI visibility не является authorization.
- Direct service boundaries защищаются через `IAccessDecisionService` или dedicated authorized wrapper.
- Role permission и license feature checks независимы.
- Administrator не обходит commercial license features.
- При отключении текущего пользователя через application service active session должна очищаться.
- Не добавляйте hardcoded passwords, private keys, generated licenses или debug bypasses.

## Archive

- Archive code не зависит от Avalonia, ReactiveUI или ViewModels.
- Не выполняйте SQL в Modbus callbacks.
- Archive queues bounded, shutdown deterministic.
- Не сериализуйте каждый Modbus register как отдельную hot-path SQL row.
- Operator views используют paged/cancelable queries.
- Raw snapshot BLOB values декодируются только для explicit details request.
- Emergency command delivery не блокируется недоступностью архива.

## RouteMap

- Topology и visual settings принадлежат definition/seed/configuration.
- `RouteMapControl` рисует и поднимает UI commands, но не владеет business logic.
- Runtime states проходят через `RouteMapRuntimeMapper`.
- Новые bindings проверяются `RouteMapConfigurationValidator`.
- Schema changes требуют migrator updates и tests.
- Не возвращайте старые rails, sensors, item lists, vehicles или bottom panels без отдельного product requirement.

## SignalId

- Names стабильны и domain-oriented.
- Не кодируйте transport details в SignalId.
- Не переименовывайте SignalId, если меняется только PLC address.
- Required command bindings обычно `Bool` и `ReadWrite`.
- `ActiveRouteFragment` всегда `Read` + `Bool`.

## Modbus

- RouteMap facade использует `Modbus.DataMap`; demo facade использует `ModbusDemo.DataMap`.
- Register-bit writes должны сохранять соседние bits через shadow/read-modify-write.
- Не делайте automatic retry для non-idempotent commands.
- Используйте `Pulse` только для подтвержденных PLC contracts.
- Потеря связи превращается в quality/stale state и terminal audit evidence, а не UI crash.

## UI и threading

- Observable Avalonia state возвращается на UI thread.
- ViewModel subscriptions dispose.
- Runtime readback не отправляет commands обратно.
- Не блокируйте `.Wait()`, task `.Result`, `Thread.Sleep`, SQL или file I/O на UI path.
- Избегайте `async void`, кроме framework event handlers.
- Передавайте cancellation tokens через service calls.

## Visual Studio visibility

- Новые projects добавляйте в `DesktopTemplate.slnx` со stable IDs.
- Documentation files попадают через `AgentDocs/AgentDocs.csproj`.
- Folder entries добавляйте, когда это улучшает структуру.
- Для новых Avalonia `.axaml` используйте локальный designer metadata pattern.
