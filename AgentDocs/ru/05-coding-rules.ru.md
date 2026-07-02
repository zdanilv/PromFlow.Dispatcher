# Правила написания кода

Эти правила описывают текущую архитектурную линию проекта. Следуйте им при изменениях
RouteMap, Modbus TCP, SignalId mapping и UI.

## Архитектурные правила

- Не добавляйте Modbus-адреса в XAML, ViewModel, `RouteMapControl` или `route-map.json`.
- UI работает с `SignalId`; физическая адресация остается в `Modbus.DataMap`.
- Не смешивайте `Modbus.DataMap` и `ModbusDemo.DataMap`.
- Не храните операторские тревоги в `Modbus.DataMap`; используйте `Modbus.AlarmMap`.
- Не переносите TCP endpoint/lifecycle из `ModbusDemo` в RouteMap.
- Не регистрируйте `RouteMapDefinition` как immutable singleton.
- Не обновляйте Avalonia UI напрямую из Modbus callback.
- Не переносите interlock, safety и окончательное разрешение команд из PLC в UI.

## RouteMap

- Топология и визуальные параметры должны жить в definition/seed/configuration, а не в
  ad hoc XAML-линиях и эллипсах.
- `RouteMapControl` должен рисовать и отдавать UI-команды, а не хранить бизнес-логику.
- Runtime-состояния должны проходить через `RouteMapRuntimeMapper`.
- Новые bindings должны валидироваться через `RouteMapConfigurationValidator`.
- Любые изменения schema требуют миграции в `RouteMapConfigurationMigrator` и тестов.
- Параметры оборудования карточки хранятся в карточке как `EquipmentParameter` binding
  с настраиваемыми `Title`, `SignalId`, `Direction` и `ValueType`.
- Не возвращайте старые rails, sensors, item-list, vehicles или bottom panel без отдельного требования.

## SignalId

- Имена должны быть стабильными и доменными.
- Не кодируйте transport details в SignalId.
- Не меняйте SignalId при изменении PLC address.
- Обязательные command bindings должны оставаться `Bool` и обычно `ReadWrite`.
- `ActiveRouteFragment` всегда `Read` + `Bool`.
- `system.fault` — системный, но PLC-mapped `Read/Bool` SignalId в `Modbus.DataMap`;
  не переносите его в `Modbus.AlarmMap`.
- `StartOffFeedback` и `StopOffFeedback` — только карточные `Read/Bool` роли. При
  `true` они отключают кнопку и сбрасывают checked-состояние без записи команды.
- `EquipmentParameter` — единственная роль для параметров оборудования карточки. Эти
  SignalId автоматически попадают в `SignalId ↔ Modbus`; не создавайте для них
  отдельный ручной список вне `RouteMapSignalInventory`.

## Modbus

- RouteMap facade использует `Modbus.DataMap`; demo facade использует `ModbusDemo.DataMap`.
- `Modbus.AlarmMap` читает монитор тревог в admin/user режимах; acknowledgement пишется
  отдельным импульсом через bit-writer, без служебных DataMap-точек.
- Правая панель уведомлений читает те же `Modbus.AlarmMap` события из
  `RouteMapSessionJournal`; не создавайте для операторских тревог служебные `SignalId`.
- Сессионная вкладка `История` остается in-memory. Будущую выгрузку в БД подключайте
  через `ISessionJournalExporter`, не из XAML и не из Modbus callback.
- В `Менеджер тревог` не смешивайте `Alarm` и `Acknowledgement`: это разные биты.
  `Repeat ms` валиден в `1000..86400000`, `Pulse ms` в `1..60000`; register bit только
  `0..15`, coil bit не задается.
- Register-bit запись должна сохранять соседние биты через shadow/read-modify-write.
- Не делайте automatic retry для неидемпотентных команд.
- `Pulse` назначайте только по подтвержденному PLC-контракту.
- При bad/stale connection не завершайте UI аварийно; отдавайте quality/stale состояние.

## UI и threading

- Все обновления Avalonia observable state должны возвращаться на UI thread.
- Подписки ViewModel должны освобождаться в `Dispose`.
- У `NotificationsPanelView` сохраняйте минимум `400`, но не фиксируйте `MaxWidth`:
  вкладка `История` должна расширять правую колонку RouteMap по ширине таблицы.
- Runtime readback не должен повторно отправлять команды.
- Диалог параметров карточки должен писать только `Write`/`ReadWrite` значения через
  `IEquipmentCommandDispatcher`; `Read` строки отображаются без редактирования, а
  `Сохранить` не закрывает диалог. Bool показывайте переключателем; остальные значения
  валидируйте по `SignalValueType`, а Modbus mapping проверяйте до `DispatchAsync`.
  В `user` режиме скрывайте техническую подпись `SignalId • Type`; в `admin` режиме
  оставляйте ее видимой для диагностики.
- При `connection.connected=false` карточки показывают `Не в сети` серым индикатором
  независимо от status/text binding.
- Взаимоисключение `ПУСК`/`СТОП` должно писать `false` в противоположную команду перед
  `true` в выбранную; snapshot-конфликт двух `true` отображается как checked только `СТОП`.
- Optimistic UI state допустим только как временное состояние до readback.

## Visual Studio visibility

- Новые проекты добавляйте в `DesktopTemplate.slnx` со стабильным `Id`.
- Для документации используйте явные `None Include/Update`, чтобы файлы были видны.
- Для новых папок добавляйте `<Folder Include="...\" />`, если Solution Explorer иначе
  может показать структуру неоднозначно.
- Для новых Avalonia `.axaml` соблюдайте локальный паттерн `<None Update="..."><SubType>Designer</SubType></None>`.

