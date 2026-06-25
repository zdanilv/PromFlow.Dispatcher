# Руководство по архиву

Этот документ описывает подсистему архива после Stage 14. Код архива находится за
контрактами Application и реализацией Persistence. Desktop и Avalonia могут вызывать
архивные сервисы, но хранение, партиционирование, запросы, экспорт, backup, retention и
health не должны зависеть от Avalonia, ReactiveUI или ViewModel.

## Владение runtime

Запуск и остановка архива централизованы в `ApplicationRuntimeCoordinator`.
Workspace ViewModel не запускает `IArchiveRuntime` и `IModbusArchiveCollector`.
Порядок запуска: инициализация persistence, installation identity, refresh лицензии,
запуск archive runtime, подписка collector и существующая политика Modbus autostart.
Остановка: dispose workspace, остановка Modbus facade/runtime, отписка collector,
flush/stop archive runtime и освобождение single-instance lease.

## Health и evidence

Основной источник состояния - `IArchiveHealthService.Observe()` и снимки
`ArchiveHealth`. В production важны поля `State`, `QueueDepth`,
`DroppedTelemetryCount`, `LastSuccessAtUtc`, `LastErrorCode` и `LastErrorMessage`.
Большая очередь или рост dropped telemetry означает, что storage не успевает, но
emergency delivery не должен блокироваться недоступностью архива.

## Запросы и UI

Archive UI работает страницами и поддерживает cancellation. Он не должен загружать всю
историю. Список snapshot сначала использует metadata query; coil/register значения
декодируются только для выбранной записи в details. Security Audit требует
`Permission.ViewSecurityAudit` и license feature `Archive`.

## Пример production config

Приложение не добавляет секцию `Archive` по умолчанию. Deployment может добавить ее с
текущей формой `ArchiveOptions`:

```json
{
  "Archive": {
    "Enabled": true,
    "DatabaseDirectory": "%LOCALAPPDATA%\\PromFlow.Dispatcher\\archive",
    "ExportDirectory": "%LOCALAPPDATA%\\PromFlow.Dispatcher\\exports",
    "BatchSize": 100,
    "FlushIntervalMs": 1000,
    "ChannelCapacity": 10000,
    "QueryMaxPageSize": 500,
    "ExportMaxRangeDays": 31,
    "ExportMaxRecords": 100000
  }
}
```

Хранилище должно быть локальным или на надежном быстром томе. SQLite partitions на
нестабильной network share допустимы только как осознанный операционный риск.

## Отказы

Недоступность архива переводит систему в degraded service, но не блокирует emergency
delivery. Retention, export и backup возвращают typed operation result. Поврежденная
старая partition должна ломать только затронутый запрос, а более поздние валидные
partitions остаются доступными.
