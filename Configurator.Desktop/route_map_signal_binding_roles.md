# Signal Binding роли RouteMap

Документ описывает роли `SignalBindingRole`, которые связывают элементы RouteMap с `SignalId` и далее с `Modbus.DataMap` на экране `SignalId ↔ Modbus`.

## Карта и маршруты

| Роль | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `State` | `Read` | `String` или числовой код | Основное состояние объекта: ожидание, готовность, выполнение, авария, offline. |
| `Visible` | `Read` | `Bool` | Управляет видимостью объекта на карте. |
| `Fault` | `Read` | `Bool` | Переводит объект в аварийное состояние при `true`. |
| `ActiveRoute` | `Read` | `Bool` | Показывает, что узел или линия входит в текущий активный маршрут. |
| `connection.status` | `Read` | `String` | Системный SignalId статуса общего Modbus Demo runtime, отображается в TopBar. |

## Узлы

| Роль | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `TargetCommand` | `ReadWrite` | `Bool` | Toggle-команда выбора узла как точки отправки. Клик пишет `true`; снятие выбора или переключение на другой узел пишет `false`. |
| `LoaderCommand` | `ReadWrite` | `Bool` | Toggle-команда выбора узла как точки возврата/загрузки. Клик пишет `true`; снятие выбора или переключение на другой узел пишет `false`. |

Если у узла `MenuKind = SendOnly`, используется `TargetCommand`. Если `MenuKind = SendAndReturn`, дополнительно используется `LoaderCommand`.

## Линии

| Роль | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `State` | `Read` | `String` или числовой код | Основное состояние линии. |
| `Visible` | `Read` | `Bool` | Показывает или скрывает линию. |
| `Fault` | `Read` | `Bool` | Помечает линию аварийной. |
| `ActiveRoute` | `Read` | `Bool` | Подсвечивает линию как часть активного маршрута. |

## Карточки

| Роль | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `State` | `Read` | `String` или числовой код | Основное состояние оборудования. |
| `Text` | `Read` | `UInt16` или `String` | Текстовый или кодовый статус карточки. |
| `Value` | `Read` | Любой поддержанный тип | Дополнительное значение для отображения. |
| `Visible` | `Read` | `Bool` | Показывает или скрывает карточку. |
| `Fault` | `Read` | `Bool` | Помечает карточку аварийной. |
| `StartCommand` | `ReadWrite` | `Bool` | Адрес команды `ПУСК`. |
| `StartOffFeedback` | `Read` | `Bool` | Необязательный сигнал PLC для сброса toggle `ПУСК` в `false`, когда равен `true`. Используется только если включен флаг `startOffFeedbackEnabled`. |
| `StopCommand` | `ReadWrite` | `Bool` | Адрес команды `СТОП`. |
| `StopOffFeedback` | `Read` | `Bool` | Необязательный сигнал PLC для сброса toggle `СТОП` в `false`, когда равен `true`. Используется только если включен флаг `stopOffFeedbackEnabled`. |

Для `Momentary` кнопок OffFeedback не используется. Для `Toggle` кнопок поведение выбирается отдельно для каждой кнопки в настройках Route Map: если OffFeedback включен, пользовательский клик пишет только `true`, а сброс приходит через `*OffFeedback`; если выключен, toggle пишет и `true`, и `false` в свой `*Command`.

## Заглушки

`PlaceholderRules` не имеют Signal Binding ролей. Они управляют только layout-логикой автозаполнения карточек: где показывать пустые места, какой высоты они должны быть и как стилизуются.

## TopBar

| Роль | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `AutomaticModeCommand` | `ReadWrite` | `Bool` | Команда автоматического режима. Включение автомата пишет `AutomaticModeCommand=true` и `ManualModeCommand=false`. |
| `ManualModeCommand` | `ReadWrite` | `Bool` | Команда ручного режима. Включение ручного режима пишет `ManualModeCommand=true` и `AutomaticModeCommand=false`. |
| `EmergencyCommand` | `ReadWrite` | `Bool` | Команда аварии. |
| `EmergencyOffFeedback` | `Read` | `Bool` | Необязательный сигнал PLC для сброса toggle `АВАРИЯ` в `false`, когда равен `true`. Используется только если включен флаг `topBar.emergency.offFeedbackEnabled`. |

`AutomaticModeCommand` и `ManualModeCommand` не используют OffFeedback-роли. Для `АВАРИЯ` поведение такое же, как у карточных toggle-кнопок: OffFeedback можно включить или выключить в настройках Route Map; для `Momentary` аварии OffFeedback не используется.

## Системные SignalId

| SignalId | Направление | Тип | Назначение |
| --- | --- | --- | --- |
| `connection.status` | `Read` | `String` | Статус общего Modbus Demo runtime, который отображается в TopBar. |

## Правило для ToggleButton

1. Узлы `Отправить`/`Возврат` и режимы `АВТОМАТ`/`РУЧНОЕ` работают по старой логике: toggle-команды пишут и `true`, и `false` в свои command-роли.
2. Карточные `ПУСК`/`СТОП` и TopBar `АВАРИЯ` имеют отдельную настройку OffFeedback на каждую кнопку.
3. При включенном OffFeedback пользовательский клик пишет только `true`; `false` приходит от PLC через соответствующую `*OffFeedback` роль.
4. При выключенном OffFeedback пользовательский toggle пишет `true/false` напрямую в существующую `*Command` роль.
