# PromFlow Dispatcher

PromFlow Dispatcher — настольное приложение для наблюдения за технологическими маршрутами и работы с оборудованием через **Route Map** и PLC по **Modbus TCP**. Основа — **.NET 10** и **Avalonia**; целевые платформы пакетов — Windows 10/11 и ALT Linux 11.1 (x86-64).

## Промышленная безопасность

> [!WARNING]
> Приложение и его интерфейс не заменяют логику безопасности PLC. Аварийные блокировки, разрешения на работу механизмов и окончательное исполнение команд должен обеспечивать контроллер. Отправляйте команды оборудованию только в разрешённой, подготовленной среде после проверки привязок сигналов и реакции PLC.

## Основные возможности

| Возможность | Что доступно сейчас |
|---|---|
| Route Map | Схема маршрута, состояния, карточки, команды и параметры оборудования с учётом readback и качества данных. |
| SignalId ↔ Modbus | Привязка доменных `SignalId` к физическим адресам PLC в административном редакторе. |
| Modbus TCP | Настройка соединения и клиентской/серверной ролей общего runtime. |
| Тревоги | Административный менеджер, уведомления и история текущей сессии в Route Map. |
| Пользовательские данные | Настройки и карты в профиле пользователя; импорт и экспорт конфигураций. |
| OPC UA | Контракты, клиент, сервер, runtime и Desktop-компоненты есть; отдельной вкладки и запуска runtime из Workspace нет. |

Boot регистрирует инфраструктуру OPC UA, но пользовательский сценарий через Workspace недоступен. Локальные тесты не подтверждают работу с промышленным endpoint; текущие `None`/`Anonymous` не дают промышленной защиты соединения. Подробности — в [руководстве OPC UA](AgentDocs/ru/10-opc-ua-guide.ru.md).

## Быстрый запуск из исходного кода

Установите .NET SDK **10.0.3xx** согласно [global.json](global.json). Из корня репозитория восстановите зависимости [DesktopTemplate.slnx](DesktopTemplate.slnx) и запустите Boot. Используйте тестовый или изолированный endpoint.

PowerShell:

```powershell
dotnet restore .\DesktopTemplate.slnx
dotnet run --project .\Configurator.Boot\Configurator.Boot.csproj
```

Bash:

```bash
dotnet restore ./DesktopTemplate.slnx
dotnet run --project ./Configurator.Boot/Configurator.Boot.csproj
```

## Установка готовых сборок

Windows: запустите установщик `PromFlow.Dispatcher-<version>-win-x64-setup.exe`. ALT Linux: в root-shell установите локальный RPM по абсолютному пути, доступному `root`:

```bash
su -
apt-get install /абсолютный/доступный/root/путь/promflow-dispatcher-1.0.0-alt1.x86_64.rpm
exit
```

Замените `1.0.0` и путь на фактические. Для запуска в Linux: `/usr/bin/promflow-dispatcher`. Требования и проверка установки — в [руководстве](AgentDocs/ru/08-build-and-install.ru.md).

## Режимы работы

Поставляемая настройка `Application.WorkMode` в [appsettings.json](Configurator.Boot/appsettings.json) выбирает видимый интерфейс:

| Режим | Интерфейс |
|---|---|
| `user` | Route Map занимает рабочую область; административные вкладки скрыты. |
| `admin` | Вкладки **Route Map**, **SignalId ↔ Modbus**, **Менеджер тревог**, **Modbus TCP**. |

Скрытие вкладок не отключает настроенный Modbus runtime и мониторинг тревог. `Application.WorkMode` не является системой авторизации или ограничением доступа к командам PLC.

## Основной сценарий

1. В режиме `admin` настройте и сохраните Route Map: маршрут, карточки, `SignalId` ([Route Map](AgentDocs/ru/02-route-map-guide.ru.md)).
2. На вкладке **SignalId ↔ Modbus** сопоставьте сигналы с PLC, проверив тип, направление и адрес ([SignalId](AgentDocs/ru/04-signal-id-guide.ru.md)).
3. На вкладке **Modbus TCP** настройте соединение и запустите роли либо автозапуск. Выбор источника `Modbus` в Route Map runtime не запускает ([Modbus TCP](AgentDocs/ru/03-modbus-tcp-guide.ru.md)).
4. Наблюдайте состояния и уведомления в Route Map; тревоги настраивайте в **Менеджере тревог**. Проверяйте команды по readback.

`Modbus.DataMap` связывает `SignalId` с адресами PLC; `Modbus.AlarmMap` описывает тревоги. `ModbusDemo.DataMap` — прежняя demo-конфигурация, не карта Route Map. `system.fault` относится к `Modbus.DataMap`. Адресация, запись и ACK — в [руководстве Modbus TCP](AgentDocs/ru/03-modbus-tcp-guide.ru.md).

## Конфигурация и пользовательские данные

Поставляемый `Configurator.Boot/appsettings.json` задаёт defaults и install-level `Application.WorkMode`; приложение его не перезаписывает. Изменяемые настройки и карты хранятся в профиле пользователя:

| Платформа | Корневой каталог пользовательских данных |
|---|---|
| Windows | `%LOCALAPPDATA%\Configurator` |
| Linux | `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator` |

В каталоге находятся `appsettings.json`, `user_settings.json` и `RouteMap/route-map.json`. Для backup закройте приложение и скопируйте каталог целиком; ограничьте доступ к копии. Обновление не должно заменять пользовательские файлы, но может заменить ручные правки defaults. Пути и восстановление — в [руководстве](AgentDocs/ru/09-configuration-file-locations.ru.md).

## Архитектура решения

| Проект | Назначение |
|---|---|
| `Configurator.Boot` | Запуск, составление зависимостей и поставляемые настройки. |
| `Configurator.Desktop` | Интерфейс Avalonia/ReactiveUI, Workspace и Route Map. |
| `Configurator.Application` | Прикладные контракты и сервисы. |
| `Configurator.Domain` | Проект ядра для зависимостей; сейчас без C#-типов. |
| `Configurator.Infrastructure` | Файловая конфигурация и сервисы ОС. |
| `Configurator.Infrastructure.Modbus` | Обмен Modbus TCP и общий runtime. |
| `Configurator.Infrastructure.OpcUa` | OPC UA client/server, browsing и runtime. |
| `Configurator.Tests.*`, `Configurator.Infrastructure.*.Tests` | Unit-, headless UI- и локальные протокольные тесты. |

Границы слоёв и фактические точки входа описаны в [обзоре архитектуры](AgentDocs/ru/01-architecture-overview.ru.md) и [карте кода](AgentDocs/ru/11-code-map.ru.md).

## Разработка и тестирование

После восстановления зависимостей из корня репозитория выполните:

PowerShell:

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

Bash:

```bash
dotnet build ./DesktopTemplate.slnx --no-restore
dotnet test ./DesktopTemplate.slnx --no-restore
```

Для выбора профильных тестов и безопасной локальной проверки используйте [руководство по тестированию](AgentDocs/ru/06-testing-and-diagnostics.ru.md) и [карту кода](AgentDocs/ru/11-code-map.ru.md). Правила работы Codex и ограничения на взаимодействие с оборудованием находятся в [AGENTS.md](AGENTS.md).

## Документация

Начните с [master-индекса](AgentDocs/ru/00-master.ru.md); подробности открывайте по задаче:

| Что нужно узнать | Какой документ открыть |
|---|---|
| Архитектура и режимы | [01 — Архитектура](AgentDocs/ru/01-architecture-overview.ru.md) |
| Route Map | [02 — Route Map](AgentDocs/ru/02-route-map-guide.ru.md) |
| Modbus TCP и тревоги | [03 — Modbus TCP](AgentDocs/ru/03-modbus-tcp-guide.ru.md) |
| SignalId | [04 — SignalId](AgentDocs/ru/04-signal-id-guide.ru.md) |
| Тестирование и диагностика | [06 — Тестирование](AgentDocs/ru/06-testing-and-diagnostics.ru.md) |
| Сборка и установка | [08 — Установка](AgentDocs/ru/08-build-and-install.ru.md) |
| Пути конфигурации | [09 — Конфигурация](AgentDocs/ru/09-configuration-file-locations.ru.md) |
| OPC UA и ограничения | [10 — OPC UA](AgentDocs/ru/10-opc-ua-guide.ru.md) |
| Где найти код и тесты | [11 — Code Map](AgentDocs/ru/11-code-map.ru.md) |

## Лицензия

Проект распространяется по [лицензии MIT](LICENSE.txt).
