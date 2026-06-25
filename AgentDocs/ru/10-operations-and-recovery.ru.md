# Operations and recovery

Этот документ собирает production acceptance практики Stage 14. Это не installer и не
deployment automation guide; он фиксирует проверки для operators и maintainers перед
production handoff.

## Startup и shutdown

`ApplicationRuntimeCoordinator` владеет startup. `DesktopShutdownCoordinator` владеет
shutdown. Не запускайте `IArchiveRuntime`, `IModbusArchiveCollector` или Modbus runtime
из workspace ViewModel. Для диагностики startup используйте runtime state и archive
health.

Lifecycle timeouts зависят от deployment. Они должны быть достаточно короткими для
deterministic shutdown и достаточно длинными для SQLite flush/Modbus disconnect:

```json
{
  "Runtime": {
    "StartupTimeoutSeconds": 20,
    "ShutdownTimeoutSeconds": 10,
    "SingleInstanceLockTimeoutSeconds": 2
  }
}
```

## Recovery checklist

1. Если пользователей нет, выполните administrator bootstrap локально.
2. Если license missing или expired, войдите как Administrator и используйте License tab.
3. Экспортируйте installation request, когда license должна быть bound к installation.
4. Установите signed license и проверьте feature list и validity dates.
5. Проверьте, что Archive health healthy или degraded с понятной причиной.
6. Проверьте RouteMap readback перед включением equipment commands.
7. Проверьте, что command audit пишет terminal success/failure для connection loss и pulse shutdown cases.

## Manual 24-hour runbook

Автоматизированный Stage 14 suite использует ускоренные deterministic runs. Для ручного
endurance run включите archive storage, держите Modbus подключенным к representative
test PLC или simulator и записывайте `ArchiveHealth` минимум каждый час. Фиксируйте
queue depth, dropped telemetry count, last success timestamp, license state и command
audit samples. В конце выполните bounded archive queries и один bounded export; не
запрашивайте full history.

## Acceptance evidence

Поддерживаемый evidence file:
`AgentDocs/implementation/STAGE14-ACCEPTANCE.md`. В нем перечислены automated scenarios,
verification commands, known limitations и статус `Stage 15: not started`.
