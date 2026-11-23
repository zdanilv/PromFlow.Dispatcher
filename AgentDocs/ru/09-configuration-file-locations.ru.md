# Расположение конфигурационных файлов

`PromFlow Dispatcher` хранит изменяемые настройки отдельно для каждого пользователя ОС. Ни Windows-установщик, ни RPM не записывают пользовательские данные в каталог установки; обновление и удаление программы не удаляют пользовательскую конфигурацию.

## Основные пути

| Назначение | Windows 10/11 | ALT Linux 11.1 и другие Linux |
|---|---|---|
| Корневой каталог данных пользователя | `%LOCALAPPDATA%\Configurator` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator` |
| Общие runtime-настройки | `%LOCALAPPDATA%\Configurator\appsettings.json` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/appsettings.json` |
| База истории Route Map | `%LOCALAPPDATA%\Configurator\history\events.db` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/history/events.db` |
| Архивы истории по умолчанию | `%LOCALAPPDATA%\Configurator\history\archives` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/history/archives` |
| Настройки окна | `%LOCALAPPDATA%\Configurator\user_settings.json` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/user_settings.json` |
| Определение RouteMap | `%LOCALAPPDATA%\Configurator\RouteMap\route-map.json` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/RouteMap/route-map.json` |
| Временный файл при сохранении RouteMap | `%LOCALAPPDATA%\Configurator\RouteMap\route-map.json.tmp` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/RouteMap/route-map.json.tmp` |
| Автозапуск текущего пользователя | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | `${XDG_CONFIG_HOME:-$HOME/.config}/autostart/promflow-dispatcher.desktop` |
| Логи — не конфигурация | `%LOCALAPPDATA%\Configurator\logs\app-YYYYMMDD.log` | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator/logs/app-YYYYMMDD.log` |

Например, для пользователя `ivan` стандартный Windows-путь начинается с `C:\Users\ivan\AppData\Local\Configurator`. В Linux, если `XDG_DATA_HOME` не задана, используется `~/.local/share/Configurator`.

## Назначение файлов

### `appsettings.json` в профиле пользователя

Это основной изменяемый файл настроек. В нём сохраняются изменённые разделы runtime-конфигурации, в том числе:

- `RouteMapRuntime` — выбранный источник сигналов и связанные параметры;
- `Modbus` — параметры Modbus TCP, `DataMap`, `AlarmMap` и метки адресов;
- `ModbusDemo` — совместимая конфигурация demo runtime;
- `OpcUa` — параметры OPC UA клиента, сервера, безопасности и адресного пространства;
- `Help` — до десяти строк контактов `{ Label, Value, Uri? }` для диалога `ПОМОЩЬ`;
- `Startup.Enabled` — регистрация приложения в автозапуске текущего пользователя;
- `HistoryArchive` — расписание, расположение и сроки хранения архивов Route Map.

Если общий файл содержит `Help`, этот раздел целиком заменяет список контактов из
поставляемого defaults-файла. Это важно для JSON-массивов: контакты из двух файлов не
смешиваются по номерам строк. Изменение `Startup.Enabled` вступает в силу при следующем
старте приложения; контакты применяются при следующем открытии диалога после reload.

Файл создаётся при первом сохранении соответствующей настройки. Он накладывается на defaults, поставляемые вместе с приложением, поэтому в нём могут находиться не все разделы базового `appsettings.json`.

### `history/events.db` и архивы Route Map

SQLite создаётся в фиксированном каталоге `history` в профиле пользователя. При работе
рядом с БД могут появляться файлы `events.db-wal`, `events.db-shm` и `events.db.lock`.
Копируйте каталог после штатного закрытия приложения.

Параметры секции `HistoryArchive` применяются после сохранения в `Настройках приложения`.

| Параметр | По умолчанию | Назначение |
|---|---|---|
| `Enabled` | `true` | Автоматическая архивация. Запись в SQLite работает при любом значении. |
| `ArchiveIntervalMinutes` | `1440` | Интервал между плановыми проверками. Пресеты в UI — 15 минут, 1 час, 6 часов, 24 часа и 7 дней. |
| `Directory` | `""` | Папка для новых ZIP. Пустое значение выбирает `history/archives`. |
| `FileNameTemplate` | `PromFlow_History_{date}_{seq}.zip` | `{date}` подставляет `yyyyMMdd_HHmmss` в UTC. `{seq}` подставляет порядковый номер. |
| `ArchiveOnExit` | `true` | Попытка записать ZIP при штатном завершении, когда `Enabled=true`. |
| `RetentionDays` | `0` | Срок хранения ZIP в сутках. При `0` автоматическое удаление отключено. |
| `LocalDatabaseRetentionDays` | `30` | Минимальный возраст строк для очистки SQLite. Для удаления требуется доступный проверенный ZIP, покрывающий эти строки. |
| `MinimumFreeSpaceMb` | `1024` | Минимум свободного места на диске для создания ZIP. |

Смена папки применяется к новым ZIP. Подтверждённые архивы учитываются по своим
исходным путям. При недоступности старого архива проверьте путь и состояние диска
по сообщению в настройках. Если срок хранения ZIP истекает раньше срока хранения строк
в SQLite, оставшиеся строки этого диапазона сохраняются в базе.

### `user_settings.json`

Содержит пользовательские настройки окна: размер, полноэкранный режим и другие параметры интерфейса, которые не относятся к общему runtime оборудования.

Старые portable-версии могли хранить `user_settings.json` рядом с исполняемым файлом или в текущем рабочем каталоге. При первом запуске новая версия читает такой legacy-файл, копирует настройки в путь из таблицы и оставляет исходный файл без изменений. Его можно удалить вручную только после проверки, что настройки загружаются из нового расположения.

### `RouteMap/route-map.json`

Содержит полное пользовательское определение RouteMap: геометрию, карточки, стили, bindings `SignalId`, параметры оборудования и настройки TopBar. Для editable-параметров карточек здесь же сохраняются локальный setpoint, pending-статус одноразовой автоотправки после Modbus reconnect и последняя ошибка отправки. Поэтому эти значения входят в экспорт/импорт RouteMap и переживают перезапуск. Физические Modbus-адреса находятся не здесь, а в разделе `Modbus.DataMap` файла `appsettings.json`.

Сохранение RouteMap атомарно: сначала создаётся `route-map.json.tmp`, затем он заменяет основной файл. Если `.tmp` остался после аварийного завершения программы, сначала закройте приложение и сохраните копию обоих файлов перед диагностикой.

## Файл defaults в каталоге установки

Вместе с приложением устанавливается базовый `appsettings.json`:

| Windows | ALT Linux |
|---|---|
| `C:\Program Files\PromFlow Dispatcher\appsettings.json` | `/opt/promflow-dispatcher/appsettings.json` |

Он содержит исходные значения, конфигурацию Serilog, `Application.WorkMode`, defaults
для `Help` и `Startup.Enabled=true`. Приложение не записывает в этот файл. Режим
`Application.WorkMode` остаётся install-level: `user` скрывает Windows-консоль, а
`admin` показывает её; в ALT Linux admin GUI-сессия открывается в `xterm`. Не
редактируйте файл для повседневной настройки: обновление или переустановка заменит
изменения, а у обычного пользователя нет прав записи в эти каталоги.

## Импорт, экспорт и резервное копирование

- Экспорт RouteMap и Modbus TCP создаёт JSON в каталоге, выбранном пользователем в файловом диалоге. Эти файлы не являются автоматически поддерживаемыми рабочими конфигурациями и не имеют фиксированного пути.
- Для резервной копии закройте приложение и скопируйте весь каталог `Configurator` из первой строки таблицы. Если выбрана внешняя папка для ZIP истории, включите её в копию отдельно.
- Для полного сброса настроек переименуйте или удалите только каталог `Configurator` конкретного пользователя после создания резервной копии. Удаление программы само по себе этот каталог не удаляет.

Пути содержат параметры сетевых подключений, карты сигналов и настройки оборудования. Ограничьте доступ к резервным копиям и не размещайте их в общедоступных каталогах без необходимости.

## Быстрая проверка

Windows PowerShell:

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Configurator" -Force -Recurse
```

ALT Linux:

```bash
find "${XDG_DATA_HOME:-$HOME/.local/share}/Configurator" -maxdepth 2 -type f -print
```
