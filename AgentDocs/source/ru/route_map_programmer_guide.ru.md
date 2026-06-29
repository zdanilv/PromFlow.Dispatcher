# RouteMap Programmer Guide

## Назначение

`RouteMap` - операторская мнемосхема маршрута бетонной линии и первая вкладка
`WorkspaceView` в `PromFlow.Dispatcher`.

В `Application.WorkMode=admin` Workspace подключает вкладки `Route Map`,
`SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus Demo`. В `Application.WorkMode=user`
Workspace показывает только `Route Map` на всю рабочую область. Экраны `Modbus TCP` и
`OPC UA` остаются в кодовой базе, но не подключаются к основному Workspace.

`Application.WorkMode` является launch-only режимом оболочки. Рабочие настройки
`RouteMapRuntime`, `Modbus` и `ModbusDemo` накладываются поверх defaults из общего
`%LOCALAPPDATA%\Configurator\appsettings.json`; скрытые в user режиме `SignalId ↔ Modbus`
и `Modbus Demo` продолжают существовать как VM/runtime и использовать те же настройки.
`Менеджер тревог` скрыт как admin-вкладка, но монитор тревог продолжает читать
`Modbus.AlarmMap`; в admin он также работает для наладки и симуляции.

Экран состоит из:

- верхней панели `TopBarView`;
- центральной карты `RouteMapControl`, которая рисует узлы, линии, подписи, hover/selection и меню узлов;
- overlay-слоя `RouteMapAttachedCardsLayer`, который размещает attached-карточки оборудования поверх карты и рисует визуальные карточки-заглушки вокруг них;
- скрытой правой панели заявок `RequestsPanelView` с `IsVisible="False"`.

Главное правило: UI работает с доменными `SignalId`, а не с Modbus-адресами. Привязка `SignalId -> Modbus register/bit` должна оставаться во внешнем application/infrastructure слое.

## Текущий Экран

Текущая карта намеренно упрощена до одной цепочки:

```text
ТУПИК -> БСУ 1 -> БСУ 2 -> ПОВОРОТ 90° -> БЕТОНОУКЛ. -> ТУПИК
```

Сейчас на карте:

- 1 цепочка `chain.bucket_route`;
- 5 узлов: `dead_end_lower`, `bsu_1`, `bsu_2`, `concrete_bucket`, `dead_end_upper`;
- 4 линии: `lower_dead_end_to_bsu1`, `active_bsu1_bsu2`, `bsu2_to_bucket`, `bucket_to_upper_dead_end`;
- `Vehicles` пустой;
- `MapEquipment` содержит одну карточку `equip.bucket`;
- правой item-list колонки оборудования, нижней панели оборудования и кнопки `ЗАПУСК ДЕЙСТВИЙ` нет.

Карточка `Кюбель Л.К.` является attached-объектом карты: она якорится к правому краю view, не накладывается на цепочку и по умолчанию центрируется по Y узла `concrete_bucket`. Свободное место сверху и снизу в этой правой колонке заполняется визуальными заглушками с таким же левым border.

Бейдж выбранного объекта остается в правом нижнем overlay-слое, но сдвинут левее карточной колонки (`Margin="0,0,335,16"`), чтобы не мешать attached-карточкам и при этом рисоваться поверх них.

## Где Что Лежит

- `Workspace/RouteMap/RouteMapDashboardView.axaml` - общий layout экрана, `RouteMapControl`, `RouteMapAttachedCardsLayer`, бейдж выбранного объекта, скрытая панель заявок.
- `Workspace/RouteMap/Controls/RouteMapControl.cs` - отрисовка карты, hover/click, selection, `MenuFlyout` узлов.
- `Workspace/RouteMap/Controls/RouteSegmentGeometry.cs` - геометрия прямых и 90-градусных дуг, длина пути и разбиение на видимые фрагменты.
- `Workspace/RouteMap/Controls/RouteMapViewportLayout.cs` - общие bounds и transform карты с зарезервированной колонкой attached-карточки.
- `Workspace/RouteMap/Panels/RouteMapAttachedCardsLayer.cs` - overlay-панель и testable helper `RouteMapAttachedCardLayout` для геометрии attached-карточек.
- `Workspace/RouteMap/Panels/EquipmentCardView.axaml` - визуал карточки `Кюбель Л.К.` и будущих attached-карточек.
- `Workspace/RouteMap/Models/RouteMapDefinition.cs` - статическая модель карты: цепочки, узлы, линии, машинки, карточки, заявки.
- `Workspace/RouteMap/Models/RouteMapSeed.cs` - seed текущей упрощенной схемы.
- `Workspace/RouteMap/Models/RouteMapRuntimeState.cs` - runtime-состояние объектов, видимость, команды, checked-состояния.
- `Workspace/RouteMap/Models/SignalBinding.cs` - связь UI-объектов с доменными сигналами.
- `Workspace/RouteMap/Services/RouteMapRuntimeMapper.cs` - преобразование `SignalValue` в `RouteMapRuntimeState`.
- `Workspace/RouteMap/Services/MockSignalProvider.cs` - mock-сигналы без ПЛК.
- `Workspace/RouteMap/ViewModels/*` - VM экрана, карточек, ролей узлов и заявок.
- `Application/Services/Signals/*` - общие контракты чтения сигналов и записи команд.

## Definition И Seed

`RouteMapDefinition` сейчас содержит:

```csharp
double LogicalWidth
double LogicalHeight
IReadOnlyList<RouteChain> Chains
IReadOnlyList<RouteNode> Nodes
IReadOnlyList<RouteSegment> Segments
IReadOnlyList<RouteVehicle> Vehicles
IReadOnlyList<EquipmentCommandCard> MapEquipment
IReadOnlyList<RequestItem> Requests
IReadOnlyList<RequestTemplateItem> RequestTemplates
RouteMapDisplaySettings? Display
IReadOnlyList<RoutePlaceholderRule>? PlaceholderRules
```

`RouteChain` - отдельный объект карты:

```csharp
string Id
double X
double Y
IReadOnlyList<string> NodeIds
IReadOnlyList<string> SegmentIds
string? AttachedEquipmentCardId
double AttachedCardRightOffset
RouteCardVerticalAnchor AttachedCardVerticalAnchor
```

Текущий seed:

- `LogicalWidth = 1200`, `LogicalHeight = 800`;
- `chain.bucket_route`: `X = 240`, `Y = 300`, `AttachedEquipmentCardId = "equip.bucket"`, `AttachedCardRightOffset = 0`;
- `dead_end_lower = (240, 500)`, `bsu_1 = (240, 400)`, `bsu_2 = (240, 300)`;
- `concrete_bucket = (550, 100)`, `dead_end_upper = (830, 100)`;
- `bsu2_to_bucket` имеет радиус `150`: выход дуги находится на X `390`, прямая до `concrete_bucket` равна `160`, а верхний прямой сегмент равен `280`;
- `AttachedCardVerticalAnchor = Node("concrete_bucket")`;
- карточка при offset `0` примыкает к правому краю view.

`AttachedCardRightOffset` хранится в пикселях view, а не в логических координатах карты.

## Узлы И Линии

Узел создается helper-методом `Node(...)`:

```csharp
Node(
    id,
    title,
    x,
    y,
    kind,
    labelOffsetX,
    labelOffsetY,
    labelPlacement,
    isLoader,
    isTarget,
    menuKind)
```

`RouteNodeLabelPlacement` поддерживает `Below` и `Right`. Offsets задаются в экранных
пикселях после transform.

Для `Below` X сдвигает центр подписи, а Y задает зазор под узлом:

```text
LabelCenterX = NodeCenterX + LabelOffsetX
LabelTop = NodeCenterY + NodeRadius + LabelOffsetY
```

Для `Right` X задает зазор от правой границы узла, а Y сдвигает вертикальный центр:

```text
LabelLeft = NodeCenterX + NodeRadius + LabelOffsetX
LabelTop = NodeCenterY - TextHeight / 2 + LabelOffsetY
```

У `dead_end_lower`, `bsu_1`, `bsu_2` используется `Right`, offsets `10,0`; у верхних
узлов — `Below`, offsets `0,6`. Многострочная подпись справа выравнивается влево и
центрируется по общей высоте, подпись снизу центрируется построчно.

Радиус каждого узла задается через `RouteNode.Style.Radius`. Если стиль отсутствует,
используется совместимое значение по умолчанию:

```csharp
new RouteNodeStyle().Radius == 15
```

Обычная линия создается через `Segment(...)`, активная - через `ActiveSegment(...)`.

`RouteSegment` дополнительно содержит:

```csharp
RouteSegmentKind Kind              // Straight или RoundedElbow90
double ArcRadius                   // логический радиус дуги
RouteElbowOrder ElbowOrder         // порядок прямых частей вокруг дуги
string? Title
double LabelOffsetX
double LabelOffsetY
IReadOnlyList<RouteSegmentActiveFragment>? ActiveFragments
```

Для `RoundedElbow90` радиус ограничивается диапазоном `0..min(|dx|, |dy|)`. Путь состоит из касательной прямой, точной четверти окружности и второй касательной прямой. Отдельный узел `ПОВОРОТ` не создается; подпись принадлежит сегменту и не участвует в hit-test.

Перед фрагментацией путь обрезается с двух сторон на `NodeRadius + EndpointGap`.
`EndpointGap` по умолчанию равен `6 px`; если после обрезки длина неположительная, линия
не рисуется. Номинальный видимый фрагмент равен `100 px`, разрыв — `6 px`. Разрыв
добавляется только тогда, когда оставшийся хвост будет не короче `100 px`. Каждый
фрагмент по умолчанию рисуется с `RouteLineCap.Round`; в настройках доступен `Flat`.

Каждая линия имеет отдельный сигнал выделения:

```text
route.<segmentId>.active
```

Если `route.<segmentId>.active == true`, mapper переводит линию в `RouteObjectState.ActiveRoute`, и `RouteMapControl` рисует ее активной синей линией.
`IsDirectional` не участвует в этом решении. Итоговый приоритет runtime-состояния:
`Offline`, затем глобальный `system.fault` или локальный `Fault`, затем `ActiveRoute`,
fallback; порядок bindings в JSON на результат не влияет.

Начиная со `schemaVersion = 10` длинная линия может иметь `ActiveFragments`: по одному
`ActiveRouteFragment` binding на каждый видимый range после логической фрагментации.
Имена по умолчанию: `route.<segmentId>.fragment_<1-based-index>.active`.
`RouteMapControl` сначала рисует базовые ranges, затем overlay активных ranges.
Line-level `ActiveRoute` подсвечивает всю линию, fragment-сигналы подсвечивают только
свои ranges, а `Fault`, `Offline` и `Disabled` остаются состояниями всей линии и
перекрывают fragment overlay.

Линии не выбираются мышью. `RouteMapHitTester` возвращает только узлы и машинки; marker для сегментов не рисуется.

## Роли Узлов И Меню

Узел имеет начальные флаги:

```csharp
bool IsLoader
bool IsTarget
RouteNodeMenuKind MenuKind
```

Цвет узла:

- `IsTarget == true` - зеленый (`RouteMapPalette.ReadyBrush`);
- `IsLoader == true` - желтый (`RouteMapPalette.WarningBrush`);
- иначе цвет берется из runtime-состояния через `RouteMapPalette.BrushForState(...)`.

Начальный seed:

- `bsu_1` имеет `IsLoader = true`;
- `concrete_bucket` имеет `IsTarget = true`;
- `dead_end_lower` и `dead_end_upper` имеют `MenuKind.None`.

Runtime-роли хранятся в `RouteMapDashboardViewModel.NodeRoleStates`. MenuFlyout меняет их
оптимистично и отправляет `TargetCommand`/`LoaderCommand`; последующий readback из
`ReadWrite` binding синхронизирует UI с фактическим состоянием контроллера.

Роли глобально уникальны:

- включение `IsLoader` у узла сбрасывает `IsLoader` у остальных узлов и `IsTarget` у выбранного узла;
- включение `IsTarget` у узла сбрасывает `IsTarget` у остальных узлов и `IsLoader` у выбранного узла;
- повторное нажатие на включенную роль выключает ее.

Текущие меню:

- `БСУ 1` и `БСУ 2` - `Отправить` и `Возврат`;
- `БЕТОНОУКЛ.` - только `Отправить`;
- оба узла `ТУПИК` - без меню.

Пункты меню создаются как `MenuItem` с `ToggleType = CheckBox`, показывают checked-состояние и закрывают меню после клика.

## Attached-Карточки

`RouteMapAttachedCardsLayer` создает `EquipmentCardView` для каждой VM из `RouteMapDashboardViewModel.MapEquipmentCards`. Реальные карточки остаются интерактивными children overlay-панели.

Позиционирование карточки:

```text
Right = ViewWidth - AttachedCardRightOffset
Left = max(ChainRightView + CardGap, Right - CardWidth)
CenterY = NodeY или ChainBounds.CenterY
```

`AttachedCardVerticalAnchor` поддерживает два режима:

- `Node` - центрирование по Y узла из `NodeId`;
- `ChainBoundsCenter` - центрирование по вертикальному центру полной геометрии цепочки.

Если `NodeId` отсутствует, не существует или не входит в цепочку, используется `ChainBoundsCenter`. На малом viewport ширина и высота карточки ограничиваются доступным местом; на обычном размере сохраняется точный Y выбранного узла.

`RouteMapControl` и `RouteMapAttachedCardsLayer` используют общий `RouteMapViewportLayout`: справа резервируется ширина карточки, offset и зазор, а bounds маршрута центрируются по горизонтали и вертикали в оставшейся области.

Текущие размеры layout-helper:

```csharp
RouteMapAttachedCardLayout.DefaultCardWidth == 295
RouteMapAttachedCardLayout.MinimumCardWidth == 250
RouteMapAttachedCardLayout.PlaceholderGap == 10
RouteMapAttachedCardLayout.CardOuterMargin == 5
```

Заглушки не имеют VM, сигналов, команд и DataContext, но правила их создания являются
частью модели (`RoutePlaceholderRule`). Bounds вычисляются автоматически вокруг реальной
карточки через `RouteMapAttachedCardLayout.CalculatePlaceholderBounds(...)`, а визуальные
элементы создаются как non-interactive `Border` внутри `RouteMapAttachedCardsLayer`.

Правила заглушек:

- используют те же `Left`, `Width` и `Height`, что и основная карточка;
- размещаются вверх от верхнего края основной карточки и вниз от нижнего края основной карточки;
- рисуются только целиком, без частичного обрезания у краев view;
- имеют прозрачный фон и настраиваемый border через свойства `RouteMapAttachedCardsLayer`;
- внешний `Margin` равен `CardOuterMargin`, чтобы border совпадал с border настоящей `EquipmentCardView`.

Устаревшие styled properties overlay-слоя оставлены для совместимости, но рабочие
настройки заглушек находятся в `RouteMapDefinition.PlaceholderRules` и JSON-профиле:

```xml
<panels:RouteMapAttachedCardsLayer
    Definition="{Binding Definition}"
    Cards="{Binding MapEquipmentCards}"
    PlaceholderBorderBrush="#C8D0D7"
    PlaceholderBorderThickness="2,0,0,0"
    PlaceholderCornerRadius="0"/>
```

Значения по умолчанию сохраняют текущий внешний вид: `PlaceholderBorderBrush = #C8D0D7`, `PlaceholderBorderThickness = 2,0,0,0`, `PlaceholderCornerRadius = 0`. Для выбора сторон используется стандартный Avalonia `Thickness`: `Left,Top,Right,Bottom`.

## Карточка Кюбель Л.К.

Карта содержит одну attached-карточку:

```csharp
Equipment("equip.bucket", "Кюбель Л.К.", canStart: true, canStop: true)
```

Она лежит в `RouteMapDefinition.MapEquipment` и привязана к `RouteChain.AttachedEquipmentCardId`.

Сигналы карточки:

```text
equip.bucket.text
equip.bucket.start
equip.bucket.stop
```

`equip.bucket.text` трактуется как числовой статус:

```text
0 -> Выключен (legacy)
1 -> Ожидание
2 -> Авария
3 -> Выполнение
4 -> Выгрузка
5 -> Загрузка
```

Неизвестное значение возвращает fallback, обычно `Выключено`.

`EquipmentCardViewModel.StatusBrush`:

```text
Выключен -> MutedTextBrush (legacy)
Ожидание -> WarningBrush
Авария -> FaultBrush
Выполнение -> ReadyBrush
Выгрузка -> ReadyBrush
Загрузка -> ReadyBrush
Выключено -> MutedTextBrush (пользовательская/legacy строка)
```

Статусный ellipse в карточке привязан к `StatusBrush`, поэтому меняет цвет так же, как текст статуса.

Кнопки `ПУСК` и `СТОП`:

- всегда рендерятся как обычные `ToggleButton`;
- занимают две равные `*`-колонки нижнего ряда карточки;
- расширяются вместе с шириной карточки;
- имеют `MinHeight = 40` и `FontSize = 18`;
- пишут `true/false` в `equip.bucket.start` и `equip.bucket.stop`;
- взаимоисключают друг друга: включение `ПУСК` сначала пишет `StopCommand=false`, затем
  `StartCommand=true`, включение `СТОП` сначала пишет `StartCommand=false`, затем
  `StopCommand=true`;
- поддерживают read-only `StartOffFeedback` и `StopOffFeedback`: `true` отключает
  соответствующую кнопку и показывает ее снятой без записи команды;
- runtime-обновление checked-состояния не отправляет команды обратно, потому что VM защищена флагом `_isApplyingRuntime`.

Цвета кнопок вычисляются во ViewModel с приоритетом `pressed > checked > normal`.
Defaults v10: `ПУСК` normal `#D0D0D0`, pressed `#949595`, checked `#3A9D5D`;
`СТОП` normal `#D95D4E`, pressed `#949595`, checked `#9E2F25`. Для каждого состояния
отдельно настраивается foreground.

Визуал карточки:

- фон прозрачный;
- левый border толщиной `2`;
- `MinWidth = 250`, `MinHeight = 141`;
- внутренний `Margin = 5`, `Padding = 12,8,6,8`;
- заголовок `Кюбель Л.К.`, `Отправить: ...` и `Возврат: ...` выровнены по левому краю кнопки `ПУСК`; статусная строка сохраняет отдельный status-dot слева;
- длинные строки сжимаются через `Viewbox StretchDirection="DownOnly"`.

`SendPointTitle` и `ReturnPointTitle` вычисляются из `NodeRoleStates` только по узлам привязанной цепочки. Если выбранной точки нет, показывается `—`.

## Runtime-Состояния И Сигналы

Основной enum объектов:

```text
Unknown
Idle
Ready
Running
ActiveRoute
Warning
Fault
Disabled
Offline
```

`RouteMapRuntimeMapper` создает `RouteObjectRuntimeState` для каждого узла, линии, vehicle и map-карточки. Если сигнал плохого качества или устарел, объект переводится в `Offline`.

`SignalBinding` связывает визуальный объект с доменным сигналом, не помещая Modbus-адрес
в UI:

- `SignalId` — стабильное имя, например `route.bsu2_to_bucket.active`;
- `Role` описывает назначение значения: авария, видимость, активный маршрут,
  текст или команда;
- `ValueType` задает ожидаемый тип (`Bool`, `String`, `UInt16` и другие);
- `Direction` определяет чтение, запись или оба направления.

Провайдер формирует словарь `SignalId -> SignalValue`, mapper интерпретирует каждое
значение по роли и создает runtime-состояние. При подключении Modbus меняется провайдер и
внешний каталог `SignalId -> address`; JSON карты, mapper и Avalonia UI остаются без
Modbus-адресов.

Поддерживаемые роли `SignalBindingRole`:

```text
Text
Value
Visible
StartCommand
StopCommand
Fault
ActiveRoute
TargetCommand
LoaderCommand
AutomaticModeCommand
ManualModeCommand
EmergencyCommand
```

Команды карточки блокируются для состояний:

```text
Offline
Fault
Disabled
```

Для разрешенных кнопок `StartCommand` и `StopCommand` обязательны как `ReadWrite Bool`:
mapper читает readback и обновляет `IsStartChecked` / `IsStopChecked`, а пользовательская
команда записывается через `IEquipmentCommandDispatcher`.

## Mock-Данные

`MockSignalProvider` каждые 2 секунды генерирует snapshot.

Mock динамически собирает уникальные bindings из актуальной definition manager. Для
`Text` создается циклический код, для `Visible` — `true`, для fault и команд — `false`.
Среди `ActiveRoute` bindings каждые две секунды
выбирается ровно один узел или сегмент в порядке `узел -> линия -> узел`. Поэтому новый объект или `SignalId` появляется в
mock snapshot без изменения жестко заданного перечня ID.

## DI И Навигация

Регистрация находится в `Configurator.Boot/Program.cs`.

Ключевые сервисы:

```csharp
services.AddSingleton(sp => new RouteMapConfigurationMapper(RouteMapSeed.Create()));
services.AddSingleton<RouteMapConfigurationStorage>();
services.AddSingleton<RouteMapConfigurationValidator>();
services.AddSingleton<RouteMapConfigurationMigrator>();
services.AddSingleton<RouteMapConfigurationManager>();
services.AddSingleton<IRouteMapRuntimeMapper<RouteMapRuntimeState>, RouteMapRuntimeMapper>();
services.AddTransient<RouteMapDashboardViewModel>();
```

Обе транспортные пары регистрируются всегда:
`MockSignalProvider`/`MockEquipmentCommandDispatcher` и
`ModbusTcpSignalValueProvider`/`ModbusTcpCommandDispatcher`. Общие интерфейсы
`ISignalValueProvider` и `IEquipmentCommandDispatcher` указывают на singleton
`IRouteMapSignalRuntime`, который горячо переключает активную пару и сохраняет
подписчиков dashboard.

`WorkspaceViewModel` получает `RouteMapDashboardViewModel`, а `WorkspaceView.axaml` отображает `RouteMapDashboardView`.

## Modbus TCP

Не добавляйте Modbus-адреса в XAML, ViewModel, `RouteMapControl` или `RouteMapSeed`.

Правильный поток:

В актуальной `schemaVersion = 10` карточные toggle-кнопки `ПУСК`/`СТОП` не поддерживают
`Momentary`, но используют активные роли `StartOffFeedback`/`StopOffFeedback` для
отключения кнопок по PLC-readback. Пользовательское включение `ПУСК` сначала пишет
`StopCommand=false`, затем `StartCommand=true`; включение `СТОП` сначала пишет
`StartCommand=false`, затем `StopCommand=true`; снятие галочки пишет только свою
команду `false`. TopBar `АВАРИЯ` не использует OffFeedback и пишет `true/false`
напрямую.

Toggle-команды узлов `Отправить`/`Возврат` и режимы TopBar `АВТОМАТ`/`РУЧНОЕ`
также пишут включение и выключение напрямую через `TargetCommand`/`LoaderCommand`
и `AutomaticModeCommand`/`ManualModeCommand`.

```text
Modbus Demo lifecycle
  -> shared Modbus runtime/client/server
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia View / RouteMapControl
```

Параметры подключения (`Host`, `Port`, `UnitId`, start addresses и counts) задаются на
экране `Modbus Demo` и хранятся в `ModbusDemo.Client`/`ModbusDemo.Server`.
Каталог `Modbus.DataMap` хранит RouteMap-соответствие доменных сигналов адресам:

```text
Name == SignalId
Area
Address
Length
Access
BitIndex
Type
WriteMode
PulseDurationMs
```

Примеры доменных сигналов:

```text
equip.bucket.text
equip.bucket.start
equip.bucket.stop
route.active_bsu1_bsu2.active
system.fault
```

`ModbusTcpSignalValueProvider` получает heartbeat snapshots через
`IModbusDataSnapshotSource`, формирует quality/stale и синтезирует `connection.status` и
`connection.connected`. При `connection.connected=false` mapper переводит все узлы и
линии в `Offline`, а UI-команды блокируются, кроме кнопки `НАСТРОЙКИ`. `system.fault`
не синтезируется provider-ом: это обычная read/bool точка `Modbus.DataMap`, которую PLC
поднимает для общей аварии всей карты.
`ModbusTcpCommandDispatcher` проверяет тип и доступ, затем выполняет latched или pulse
запись через `IModbusTcpService`. Bool внутри Holding Register записывается защищенным
read-modify-write. UI при этом не меняется: он продолжает получать `SignalValue` и
отправлять `SignalWriteRequest`.

`ModbusDemo.DataMap` используется только экраном `Modbus Demo`. RouteMap hot-apply меняет
только `Modbus.DataMap` и не трогает demo-карту.
Операторские аварии, повторные подтверждения и обычные сообщения хранятся отдельно в `Modbus.AlarmMap`:
они не являются RouteMap `SignalId` и не попадают в `ModbusTcpSignalValueProvider`.

`Менеджер тревог` редактирует `ModbusAlarmOptions`. UI-колонки соответствуют модели так:

| UI | Модель | Назначение |
|---|---|---|
| `Вкл.` | `Enabled` | Участвует ли строка в мониторинге тревог |
| `Id` | `Id` | Уникальный ключ состояния тревоги |
| `Тип` | `Kind` | `Fault`, `Confirmation` или `Message`, влияет на визуальный стиль диалога |
| `Сообщение` | `Message` | Текст модального уведомления |
| `Alarm area/Offset/Bit` | `Alarm.Area/Address/BitIndex` | Входной бит, где `Address` — zero-based offset, а `BitIndex` нужен только для `HoldingRegister` |
| `Alarm client/server` | вычисляется из `Alarm` | Физический адрес по start address client/server endpoint; setter пересчитывает area и offset |
| `OK area/Offset/Bit` | `Acknowledgement.Area/Address/BitIndex` | Отдельный бит подтверждения |
| `OK client/server` | вычисляется из `Acknowledgement` | Физический адрес acknowledgement-бита |
| `Repeat ms` | `RepeatIntervalMs` | Период повторного показа при активном alarm-бите |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | Время между записью `true` и `false` в acknowledgement-бит |

Валидатор требует непустые уникальные `Id`, непустой `Message`, разные `Alarm` и
`Acknowledgement`, `BitIndex=0..15` для `HoldingRegister`, `RepeatIntervalMs` в
`1000..86400000` и `AcknowledgementPulseDurationMs` в `1..60000`. Диапазоны адресов
берутся из endpoint-настроек `ModbusDemo`.

Начальный источник читается при запуске, но может быть горячо изменен в редакторе:

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

`ПРИМЕНИТЬ` меняет источник только для текущей сессии. `СОХРАНИТЬ` также обновляет
секцию `RouteMapRuntime` в общем `%LOCALAPPDATA%\Configurator\appsettings.json`.
Переключение на Modbus само по себе не запускает соединение.

Подробности находятся в `modbus_tcp_integration_guide.md`.

## Тесты

Фокусные тесты лежат в `Configurator.Tests.Unit/RouteMap`.

Они проверяют:

- seed Г-образной карты: 5 узлов, 4 линии, дуга с радиусом `150`, интервалы БСУ `100`, прямые участки после дуги `160` и `280`, пустые `Vehicles`;
- `RouteChain` с attached-карточкой `equip.bucket`, offset и вертикальным якорем;
- точную геометрию дуги, ограничение радиуса и разбиение пути на фрагменты `100 px` с разрывом `6 px`;
- центрирование маршрута слева от карточной колонки;
- стабильность масштаба при сокращении route bounds и неизменной логической области;
- правое вертикально центрированное положение подписей левой группы и нижнее положение верхних подписей;
- endpoint-gap и одинаковое обрезание прямых/дуговых путей до фрагментации;
- круглые и плоские края линии;
- layout attached-карточки: right-edge anchor, зазор от цепочки, привязку к узлу, fallback к центру цепочки и clamp малого viewport;
- layout заглушек attached-карточек: заполнение сверху/снизу, совпадение `Left`/`Width`/`Height` с основной карточкой и отсутствие частично обрезанных заглушек;
- настройки border заглушек: default brush/thickness/corner radius, поддержка разных сторон через `Thickness`, применение настроек к уже созданным placeholder-контролам;
- hit-test узлов и пустой области;
- отсутствие hit-test линий;
- маппинг active-route сигналов;
- числовые статусы `0..5` для `equip.bucket.text`;
- `StatusBrush` для известных статусов;
- checked-состояние `ПУСК` / `СТОП`;
- запись `true/false` для `equip.bucket.start` и `equip.bucket.stop`;
- взаимоисключение `ПУСК` / `СТОП`, Stop-приоритет при конфликтном readback и OffFeedback-блокировку;
- обычную toggle-команду TopBar `АВАРИЯ`;
- отображение `Отправить` / `Возврат` в карточке из выбранных ролей узлов;
- уникальность ролей `IsLoader` и `IsTarget`.
- последовательные миграции v1→v2→v3 и v2→v3 с сохранением пользовательских свойств;
- `ActiveRoute` binding у каждой линии, приоритет runtime-состояний и циклический mock.

Headless layout-тесты находятся в отдельном проекте
`Configurator.Tests.RouteMap.Ui`. Разделение необходимо, потому что
`Avalonia.Headless.XUnit 12` работает с xUnit v3, а основной unit-проект использует
xUnit v2. UI-набор открывает диалог `1320x780`, проверяет пять колонок bindings на
вкладках узлов, линий и карточек, горизонтальный scroll при узкой ширине, список вкладок
Workspace и успешный layout всех семи вкладок настроек.

Проверка сборки целевого приложения:

```powershell
dotnet build .\Configurator.Boot\Configurator.Boot.csproj --no-restore
```

Проверка RouteMap-тестов с обходом старого Designer-долга:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter FullyQualifiedName~RouteMap "-p:DefaultItemExcludes=**\Designer\**\*.cs"
```

Проверка headless UI:

```powershell
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

Весь test-проект может падать до выполнения RouteMap-тестов из-за старых `Configurator.Tests.Unit/Designer/*`, которые ссылаются на отсутствующий namespace `Configurator.Desktop.Workspace.Designer`. Это внешний технический долг, не ошибка RouteMap.

## Что Не Делать

- Не добавлять Modbus-адреса прямо в XAML, VM или `RouteMapControl`.
- Не рисовать центральные маршруты через XAML `Line`, `Ellipse` и `Canvas`; топология должна оставаться в definition/seed.
- Не возвращать старые рельсы, sensor-узлы, тележки, нижнюю панель и лишние карточки без отдельного требования.
- Не возвращать right item-list панель оборудования и кнопку `ЗАПУСК ДЕЙСТВИЙ` без отдельного требования.
- Не делать линии selectable без явного изменения hit-test, selection marker и тестов.
- Не хранить бизнес-логику маршрутов в `RouteMapControl`; контрол должен только отображать и отдавать UI-команды.
- Не менять цвета точечно в разных местах, если изменение относится к общему визуальному языку; используйте `RouteMapPalette`.
- Не обновлять UI напрямую из Modbus-потока; поток должен идти через `ISignalValueProvider` и mapper.

## Редактор Настроек RouteMap

В `TopBarView` рядом с кнопкой `АВАРИЯ` находится кнопка `НАСТРОЙКИ`. Команда
`TopBarViewModel.OpenSettingsCommand` открывает `RouteMapSettingsDialog` поверх корневого
`DialogHost`. В `Application.WorkMode=user` кнопка скрыта; в `admin` видна и остается
доступной даже при недоступной Modbus-связи.

Диалог содержит семь вкладок:

- `Источник данных` — флаг Mock-симуляции, текущий и выбранный источник, сессионное
  применение и сохранение `RouteMapRuntime`;
- `Карта и маршруты` — логические размеры, padding, карточный gap, общая фрагментация,
  палитра и упорядоченные `NodeIds`/`SegmentIds` цепочек;
- `TopBar` — тексты, normal/pressed/checked фон, foreground и командные bindings
  кнопок `АВТОМАТ`, `РУЧНОЙ`, `АВАРИЯ`;
- `Узлы` — геометрия, placement подписи, тип, меню, начальные роли, видимость, стиль и bindings;
- `Линии` — endpoints, геометрия, радиус и порядок дуги, endpoint-gap, line-cap,
  подпись, цвета, толщины, индивидуальная фрагментация, bindings и секция `Отрезки`;
- `Карточки` — фиксированный шаблон карточки, данные, команды `ПУСК`/`СТОП`,
  размеры, отступы, цвета фона и текста normal/pressed/checked, подписи кнопок,
  цепочка и вертикальный якорь;
- `Заглушки` — правила автоматического заполнения `Above`, `Below` или `Both`, режим
  высоты, gap, лимит количества и стиль.

Списковые вкладки поддерживают поиск, добавление, дублирование и удаление. При
переименовании ID редактор автоматически меняет структурные ссылки, но не меняет
`SignalId`. Удаление объекта блокируется, пока на него ссылаются другие объекты; список
зависимостей выводится в общем error banner. Modbus-адресов в DTO и XAML редактора нет.

### JSON-Контракт

Корневой DTO — `RouteMapConfigurationDocument`. Текущая версия:

```json
{
  "schemaVersion": 10,
  "map": {},
  "topBar": {},
  "chains": [],
  "nodes": [],
  "segments": [],
  "cards": [],
  "placeholderRules": []
}
```

JSON не сериализует Avalonia-типы. Цвета записываются как `#RRGGBB` или `#AARRGGBB`,
отступы и радиусы углов представлены собственными числовыми DTO. Enum записываются
строками в camelCase. `Requests`, `RequestTemplates` и `Vehicles` не входят в профиль и
при mapping берутся из `RouteMapSeed`.

Активный файл:

```text
%LocalAppData%\Configurator\RouteMap\route-map.json
```

`RouteMapConfigurationStorage` читает и форматированно записывает JSON. Запись идет во
временный файл рядом с целевым с последующей заменой, поэтому manager не публикует
частично записанный документ. При отсутствии файла используется seed. Поврежденный или
несовместимый файл не перезаписывается: приложение продолжает работать с seed, а текст
ошибки доступен через `RouteMapConfigurationManager.LastLoadError` и показывается в
диалоге.

Перед валидацией документ проходит через последовательный `RouteMapConfigurationMigrator`.
Шаг v1→v2 обновляет геометрию штатной цепочки. Шаг v2→v3 добавляет placement подписей,
endpoint-gap, round-cap и отсутствующий `ActiveRoute` binding каждой линии, не заменяя
пользовательский binding той же роли. Пользовательские размеры, цвета, карточки и
заглушки сохраняются.
Шаг v3→v4 добавляет конфигурацию TopBar, сигнальный контур узлов и обязательные bindings
`ActiveRoute`, `TargetCommand` и `LoaderCommand`, сохраняя существующие пользовательские
`SignalId` для уже известных ролей.
Шаг v4→v5 добавляет `startButtonKind`, `stopButtonKind` и
`topBar.emergency.buttonKind`, выставляя `Toggle`, чтобы старые профили визуально не
изменились.
Шаги v6→v9 удаляют legacy off-feedback/state роли и приводят карточные/аварийную
кнопки к обычному toggle-readback. Шаг v9→v10 добавляет pressed/foreground поля,
обновляет старые дефолтные цвета `АВАРИЯ`, `ПУСК` и `СТОП`, сохраняя пользовательские
цвета, и генерирует `ActiveRouteFragment` bindings для split-линий.
После успешной валидации мигрированный активный профиль атомарно сохраняется. При ошибке
исходный файл остается без изменений, manager использует seed и публикует текст ошибки.

### Команды Диалога

- `ПРИМЕНИТЬ` валидирует черновик и публикует новую definition без записи файла;
- `СОХРАНИТЬ` валидирует, записывает активный JSON и затем публикует definition;
- `ПЕРЕЗАГРУЗИТЬ` заново читает активный файл в черновик;
- `ИМПОРТ` читает выбранный `.json` только в черновик;
- `ЭКСПОРТ` записывает текущий валидный черновик в выбранный `.json`;
- `ЗАКРЫТЬ` отбрасывает только непримененные изменения. Ранее выполненный `ПРИМЕНИТЬ`
  не откатывается.

Импорт и экспорт используют `MainWindow.StorageProvider` и фильтр `*.json`.

### Manager И Горячее Применение

`RouteMapConfigurationManager` — singleton и единственный владелец актуальных
`CurrentDocument` и `CurrentDefinition`. `DefinitionChanges` публикует только полностью
проверенные snapshots. Невалидный `Apply` не меняет текущую карту и ничего не публикует.
Начальная загрузка активного профиля выполняется синхронными методами storage до создания
`MainWindow`; нельзя блокировать Avalonia dispatcher через `LoadActiveAsync().GetResult()`,
иначе приложение зависнет до показа окна. Асинхронные методы используются командами
диалога уже после запуска UI.

`RouteMapDashboardViewModel` подписан на `DefinitionChanges`. При обновлении он:

1. заменяет bindable `Definition`;
2. пересоздает карточки и начальные роли узлов;
3. очищает выбор удаленного объекта;
4. повторно маппит последний snapshot сигналов;
5. заставляет map control и attached layer пересчитать layout по новой definition.

`RouteMapRuntimeMapper` и `MockSignalProvider` получают manager через DI и читают его
актуальную definition. Mock-провайдер строит значения из фактических bindings, поэтому
добавление нового `SignalId` не требует правки жестко заданного списка.

Эффективная видимость узла, линии и карточки равна статическому `IsVisible` из JSON с
учетом runtime binding роли `Visible`. Параметры `RouteMapPalette`, радиусы узлов, pens
линий, fragment length/gap, viewport и card layout также читаются из текущей definition.

Масштаб viewport рассчитывается по `LogicalWidth x LogicalHeight`, а не по текущим bounds
маршрута. Если фактические bounds больше логической области, берется больший размер, чтобы
не обрезать схему. После выбора масштаба фактические bounds центрируются в области слева
от карточной колонки. Поэтому сокращение координат не увеличивает карту; явный способ
изменить масштаб — изменить логические размеры профиля.

Вкладки узлов, линий и карточек используют общий `SignalBindingsEditor`. Его строка
имеет явные колонки `Role`, `SignalId`, `Direction`, `ValueType`, `Удалить`; для узкого
viewport предусмотрен горизонтальный `ScrollViewer`.

### Валидация

`RouteMapConfigurationValidator` проверяет:

- `schemaVersion`, обязательность и уникальность ID;
- глобальную уникальность ID runtime-объектов;
- endpoints линий, элементы цепочек, card/chain и node-anchor ссылки;
- не более одной карточки на цепочку;
- допустимость ролей bindings, обязательный `SignalId`, направления команд и дубли ролей;
- что `ActiveRouteFragment` bindings линии соответствуют текущим logical ranges и имеют
  `Direction=Read`, `ValueType=Bool`;
- единственность начальных `IsLoader` и `IsTarget`;
- конечность координат, положительные размеры и формат цветов;
- что `ПУСК`, `СТОП` и `АВАРИЯ` используют `RouteCommandButtonKind.Toggle`;
- что `StartOffFeedback`/`StopOffFeedback` допустимы только у карточек, имеют `Direction=Read` и `ValueType=Bool`;
- радиус дуги `0..min(|dx|, |dy|)`;
- неотрицательный endpoint-gap;
- ссылки правил заглушек и их размеры/лимиты.

Ошибки показываются общей сводкой и у соответствующей строки списка. До исправления
ошибок документ нельзя применить, сохранить или экспортировать.

### Актуальная DI-Регистрация

Не регистрируйте `RouteMapDefinition` как неизменяемый singleton. Используется следующая
цепочка:

```csharp
services.AddSingleton(sp => new RouteMapConfigurationMapper(RouteMapSeed.Create()));
services.AddSingleton<RouteMapConfigurationStorage>();
services.AddSingleton<RouteMapConfigurationValidator>();
services.AddSingleton<RouteMapConfigurationMigrator>();
services.AddSingleton<RouteMapConfigurationManager>();
services.AddSingleton<IRouteMapSettingsFilePicker, RouteMapSettingsFilePicker>();
services.AddSingleton<IRouteMapSettingsDialogService, RouteMapSettingsDialogService>();
services.AddSingleton<IRouteMapRuntimeMapper<RouteMapRuntimeState>, RouteMapRuntimeMapper>();
services.AddTransient<RouteMapDashboardViewModel>();
```

RouteMap unit-проект в `PromFlow.Dispatcher` содержит только актуальный набор RouteMap и
не включает старые Designer-тесты:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore `
  --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
```

Текущий radius дуги seed равен `150`; это значение является fallback-значением JSON.

## Командные Bindings И Подсветка Узлов

Начиная со `schemaVersion = 4`, TopBar входит в `RouteMapConfigurationDocument`. Вкладка
`TopBar` редактора позволяет менять тексты, normal/pressed/checked фон, foreground и
bindings кнопок `АВТОМАТ`, `РУЧНОЙ`, `АВАРИЯ`. Телефон, логотип, пользователь и
статусная область остаются частью фиксированного XAML-шаблона. Для `АВАРИЯ` defaults v10:
normal `#D95D4E`, pressed `#949595`, checked `#9E2F25`, foreground `#FFFFFF`.

В актуальной `schemaVersion = 10` `ПУСК`, `СТОП` и `АВАРИЯ` всегда работают как
`ToggleButton`. `ПУСК` и `СТОП` взаимоисключаются: включение одной кнопки сначала
снимает противоположную команду, затем пишет `true` в свою; ручное снятие пишет только
свою команду `false`. Если PLC readback вернул оба command-бита `true`, UI показывает
только `СТОП` и не пишет исправление обратно в PLC. `АВАРИЯ` пишет `true` при включении
и `false` при снятии. Legacy-значение `RouteCommandButtonKind.Momentary` остается
только для безопасной десериализации старых JSON и миграцией приводится к `Toggle`.
`АВТОМАТ` и `РУЧНОЙ` остаются взаимоисключающими toggle-кнопками.

Обязательные командные роли:

```text
AutomaticModeCommand  -> system.mode.automatic
ManualModeCommand     -> system.mode.manual
EmergencyCommand      -> system.emergency
StartCommand           -> кнопка ПУСК карточки
StopCommand            -> кнопка СТОП карточки
StartOffFeedback       -> read-only отключение ПУСК карточки
StopOffFeedback        -> read-only отключение СТОП карточки
TargetCommand          -> пункт Отправить узла
LoaderCommand          -> пункт Возврат узла
```

Командные роли используют `Bool` и `ReadWrite`; `StartOffFeedback`/`StopOffFeedback`
используют `Bool` и `Read`. UI сначала оптимистично меняет checked-состояние, затем
отправляет `SignalWriteRequest` через `IEquipmentCommandDispatcher`. Для выбора узла
записываются все изменения: прежняя роль получает `false`, новая — `true`, а
взаимоисключающая роль выбранного узла при необходимости также сбрасывается. Входное
значение `ReadWrite` и OffFeedback применяется как readback и не вызывает повторной
записи.

Каждый узел и сегмент имеет отдельный `ActiveRoute` binding. Для узла стандартный ID:

```text
route.node.<nodeId>.active
```

Активность узла хранится в `RouteObjectRuntimeState.IsSignalActive` и рисуется внешним
контуром `ActiveOutlineColor/ActiveOutlineThickness`. Она не заменяет loader/target-заливку.
Hover и выбор мышью рисуются отдельными внешними кольцами. При `Offline`, `Fault` или
`Disabled` сигнальный контур скрывается.

Для split-линий дополнительно существуют `ActiveRouteFragment` bindings вида
`route.<segmentId>.fragment_<n>.active`. Они участвуют в mock, SignalId inventory и
Modbus diagnostics как обычные read/bool сигналы, но не заменяют общий `Fault` линии.

Mock каждые две секунды активирует ровно один объект в порядке цепочки:
`узел, исходящая линия, следующий узел, ...`. После цепочек добавляются узлы и линии, которые
не входят ни в одну цепочку. TopBar и MenuFlyout также генерируются из фактических bindings,
поэтому изменение `SignalId` через `ПРИМЕНИТЬ` начинает действовать без перезапуска.
`MockSignalState` связывает mock dispatcher и provider: выполненная запись возвращается в
следующем snapshot как readback и не отменяет оптимистическое состояние UI.

`SignalBindingsEditor` получает разрешенный список ролей от вкладки. Обязательные bindings
создаются при добавлении объекта и перед Apply/Save; удалить их, пока соответствующая кнопка
или MenuKind включены, нельзя. Подробная схема подключения реального транспорта приведена в
`modbus_tcp_integration_guide.md`.
