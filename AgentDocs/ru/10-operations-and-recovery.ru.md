# Эксплуатация и восстановление

Этот документ собирает production acceptance практики Stage 14. Это не installer и не
автоматизация deployment; это список проверок, которые operator или maintainer выполняет
перед передачей системы в эксплуатацию.

## Startup и shutdown

`ApplicationRuntimeCoordinator` владеет startup. `DesktopShutdownCoordinator` владеет
shutdown. Не запускайте `IArchiveRuntime`, `IModbusArchiveCollector` или Modbus runtime
из workspace ViewModel. Для диагностики startup используйте runtime state и archive
health.

Timeouts lifecycle зависят от deployment. Они должны быть достаточно короткими для
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
2. Если license missing или expired, войдите как Administrator и используйте вкладку `License`.
3. Экспортируйте installation request, если license должна быть привязана к этой installation.
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

## Восстановление Visual Studio Avalonia Preview

Если Visual Studio показывает ошибку вида
`AvaloniaUI.VisualStudio.Extension.Views.AvaloniaEditorWithPreview` или пишет, что
`/AvaloniaUI.VisualStudio.Extension;component/views/avaloniaeditorwithpreview.axaml`
не найден, это проблема расширения Avalonia для Visual Studio или его cache. Этот URI
относится к Visual Studio extension, а не к `.axaml` файлам PromFlow Dispatcher.

Что сделать:

1. Полностью закройте все окна Visual Studio.
2. Обновите или переустановите расширение Avalonia for Visual Studio.
3. Удалите cache компонентов Visual Studio:
   `%LOCALAPPDATA%\Microsoft\VisualStudio\*\ComponentModelCache`.
4. Снова откройте `DesktopTemplate.slnx`.
5. Если designer всё ещё не открывается, проверяйте XAML из командной строки:
   `dotnet build .\DesktopTemplate.slnx --no-restore` и headless UI tests.

Приложение не зависит от Visual Studio designer во время работы. Если build и headless
UI tests проходят, значит XAML проекта компилируется и рендерится, даже если preview в
IDE сломан.
