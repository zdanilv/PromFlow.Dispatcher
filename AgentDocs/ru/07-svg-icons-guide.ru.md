# UI-иконки в Configurator.Desktop: Material Icons и SVG

## Принятое решение

В приложении используются два дополняющих способа отображения векторных иконок:

- `Material.Icons.Avalonia` — основной выбор для типовых пиктограмм интерфейса из
  набора Material Design: телефон, питание, настройки, запуск, обновление и т. п.;
- `Svg.Controls.Skia.Avalonia` и встроенные SVG — для брендовых, уникальных или
  многоцветных изображений, которых нет в Material Icons.

Стандартные Material-иконки не выгружать в `Configurator.Desktop/Assets/icons` и не
создавать для них SVG- или PNG-копии. Пакет передает их как SVG Path, дает типобезопасное
свойство `Kind` и не требует ручного управления файлами. Собственные SVG, напротив,
остаются встроенными `AvaloniaResource`: они доступны в опубликованном приложении и
не зависят от текущей рабочей папки.

Текущая версия `Material.Icons.Avalonia` — `3.0.2`; ссылка на официальный контракт и
все доступные варианты использования: <https://github.com/SKProCH/Material.Icons#getting-started>.

Для монохромной пользовательской SVG-иконки применяем `<svg:Svg>`, а не `<Image>` с
`SvgImage`. У `Svg` есть `CurrentColor`, поэтому один экземпляр иконки может корректно
менять цвет по теме, disabled- или command-state. `SvgImage` подходит, когда
изображение нужно там, где ожидается `IImage`, но не является стандартным способом
для перекрашиваемых UI-иконок.

## Material Icons в Avalonia

### Подключение уже выполнено

В `Configurator.Desktop.csproj` уже подключен пакет:

```xml
<PackageReference Include="Material.Icons.Avalonia" Version="3.0.2" />
```

Для версий 2.0.0 и выше его стили должны быть зарегистрированы один раз на уровне
приложения. Это уже сделано в `Configurator.Desktop/App.axaml`; не добавлять
`MaterialIconStyles` повторно в view или локальный словарь ресурсов:

```xml
<Application xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             ...>
  <Application.Styles>
    ...
    <materialIcons:MaterialIconStyles />
  </Application.Styles>
</Application>
```

В XAML-файле, где нужна иконка, объявить пространство имен на корневом элементе:

```xml
xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
```

`Kind` выбирает значение типобезопасного перечисления `MaterialIconKind`. Пользоваться
автодополнением IDE или каталогом Material Design Icons; не передавать путь к SVG и не
собирать имя иконки строковой конкатенацией.

### Обычный контрол

Если иконка является отдельным визуальным элементом, использовать `MaterialIcon`.
Его цвет задает `Foreground`; если свойство не задано, оно наследуется от контейнера.
Размер задавать в месте использования через `Width` и/или `Height`:

```xml
<materialIcons:MaterialIcon Kind="Phone"
                            Width="18"
                            Height="18"
                            Foreground="#003CA3" />
```

Для цвета, который зависит от темы или состояния, можно оставить наследование от
`Foreground` родительской кнопки/контейнера либо передать `IBrush` или ресурс кисти
явно:

```xml
<Button Foreground="#FFFFFFFF">
  <materialIcons:MaterialIcon Kind="PowerStandby"
                              Width="20"
                              Height="20" />
</Button>
```

### Иконка в `Content`

Для `Button.Content`, `ToggleButton.Content` и аналогичных свойств использовать
markup extension `MaterialIconExt`, а не создавать SVG-файл только ради кнопки.
Размер extension задается свойством `Size`:

```xml
<Button Content="{materialIcons:MaterialIconExt Kind=TuneVerticalVariant, Size=22}" />
```

Если для extension требуется цвет, отличный от обычного `Foreground` контейнера,
передать его через `IconForeground`:

```xml
<Button Content="{materialIcons:MaterialIconExt Kind=PowerStandby,
                 Size=22,
                 IconForeground=#FFFFFFFF}" />
```

Для кнопки с единственным `Content` — пиктограммой и подписью — использовать
`MaterialIconTextExt`:

```xml
<Button Content="{materialIcons:MaterialIconTextExt Kind=Play, Text=Запуск}" />
```

Не заменять текстовые команды `ПУСК`, `СТОП`, `СБРОС` и их контракты RouteMap
иконками без отдельного UX-решения: библиотека отвечает только за визуальное
представление.

### Использование как `Image.Source`

`MaterialIcon` реализует `IImage`, поэтому extension можно передать в `Image.Source`:

```xml
<Image Width="24"
       Height="24"
       Source="{materialIcons:MaterialIconExt Kind=Abacus,
               IconForeground=DeepPink}" />
```

В этом варианте размеры задаются у `<Image>`; значения размера на `MaterialIcon`
не влияют на результат. Предопределенные анимации `MaterialIcon` в режиме источника
изображения не поддерживаются. Если нужна анимация иконки, использовать визуальный
контрол `MaterialIcon`, а не `Image.Source`.

## Встроенные SVG

## Хранение и упаковка

Иконки располагаются по стилю:

```text
Configurator.Desktop/
  Assets/icons/
    outline/  # контурные, монохромные
    filled/   # залитые, монохромные или многоцветные
```

В `Configurator.Desktop.csproj` уже есть правило:

```xml
<AvaloniaResource Include="Assets\icons\**" />
```

Новые SVG под это правило попадают автоматически. Не добавлять для них
`CopyToOutputDirectory`: в UI нужно обращаться к встроенному ресурсу, а не к файлу
рядом с `.exe`.

Использовать полный URI ресурса, чтобы загрузка не зависела от того, в какой view
расположена разметка:

```text
avares://Configurator.Desktop/Assets/icons/outline/user.svg
```

Регистр символов и путь должны в точности соответствовать файлу. Это особенно важно
при публикации под Linux и macOS.

## Контракт SVG

Каждая UI-иконка должна иметь `viewBox`, не содержать внешних зависимостей и быть
самодостаточной. Не использовать `script`, внешние CSS/шрифты, сетевые или файловые
ссылки (`href` на внешний файл), а также растровые изображения внутри иконки.

Для контурной монохромной иконки используем `currentColor` в `stroke`:

```xml
<svg xmlns="http://www.w3.org/2000/svg"
     width="24" height="24" viewBox="0 0 24 24"
     fill="none" stroke="currentColor" stroke-width="2"
     stroke-linecap="round" stroke-linejoin="round">
  <path stroke="none" d="M0 0h24v24H0z" fill="none" />
  <!-- геометрия иконки -->
</svg>
```

Для залитой монохромной иконки используем `fill="currentColor"`; для нее `stroke`
обычно не нужен. Не задавать в таких иконках фиксированные `#RRGGBB` на `path`, если
цвет должен подчиняться теме: `CurrentColor` меняет только SVG-свойства,
использующие `currentColor`.

Многоцветная иконка — исключение. Ее постоянные цвета можно оставить в SVG. Если
один из ее слоев должен меняться, присвоить этому слою уникальный CSS-класс и
передать CSS через свойство `Css` контрола; не модифицировать файл на лету.

## Разбор user.svg

`Assets/icons/outline/user.svg` уже соответствует контракту контурной иконки:

- `viewBox="0 0 24 24"` задает независимую от размера геометрию;
- `fill="none"` предотвращает нежелательную заливку;
- `stroke="currentColor"` делает цвет линий настраиваемым;
- ширина линии, скругления и соединения определены один раз на корневом `<svg>`.

Менять `stroke="currentColor"` на жесткий цвет в самом файле не нужно: это породит
дубликаты иконки для normal/hover/disabled/selected состояний.

## Обычное применение в XAML

Объявить пространство имен один раз в view или в базовом словаре:

```xml
xmlns:svg="using:Avalonia.Svg.Skia"
```

Затем использовать иконку так:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         Width="20"
         Height="20"
         Stretch="Uniform"
         CurrentColor="#FF526173"
         EnableCache="True" />
```

`Width` и `Height` задаются местом использования, а не меняются в файле иконки.
Обычные размеры: 16, 20 или 24 device-independent pixels. `Stretch="Uniform"`
сохраняет пропорции.

`EnableCache="True"` следует включать для обычных статических UI-иконок: контрол
кеширует отрисовку и не должен заново подготавливать ее на каждом кадре.

## Цвета темы и состояния

В ресурсах приложения или view объявляются именно `Color`-значения:

```xml
<Application.Resources>
  <Color x:Key="Icon.Foreground">#FF526173</Color>
  <Color x:Key="Icon.OnAccent">#FFFFFFFF</Color>
  <Color x:Key="Icon.Disabled">#FF9CA4AA</Color>
</Application.Resources>
```

Иконка получает цвет как динамический ресурс:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         Width="20"
         Height="20"
         Stretch="Uniform"
         CurrentColor="{DynamicResource Icon.Foreground}"
         EnableCache="True" />
```

`CurrentColor` имеет тип `Avalonia.Media.Color?`, поэтому не передавать ему
`IBrush` или `SolidColorBrush`, используемые свойством `Foreground`. Для цвета,
вычисляемого во ViewModel, публиковать `Color` (например, `IconColor`) и привязать
его напрямую:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         CurrentColor="{Binding IconColor}"
         Width="20" Height="20" Stretch="Uniform" EnableCache="True" />
```

`Foreground` кнопки или контейнера сам по себе не перекрашивает внешний SVG.
Для файла с `currentColor` всегда устанавливать `CurrentColor` на `<svg:Svg>`
(или унаследованно на его предке).

Пример для кнопки с контрастной иконкой:

```xml
<Button Background="#FF2F80ED">
  <svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
           Width="16" Height="16"
           Stretch="Uniform"
           CurrentColor="{DynamicResource Icon.OnAccent}"
           EnableCache="True" />
</Button>
```

## Необычные случаи

Если нужно заменить один из фиксированных цветов, в SVG добавить класс к конкретному
слою и передать правило через `Css`:

```xml
<path class="icon-accent" d="..." />
```

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/filled/example.svg"
         Css=".icon-accent { fill: #FF2F80ED; }"
         Width="20" Height="20" Stretch="Uniform" EnableCache="True" />
```

Для простой монохромной иконки `Css` не нужен: `currentColor` и `CurrentColor`
проще, надежнее и понятнее. Не загружать SVG через абсолютный путь файловой системы,
не менять текст SVG для каждого состояния и не создавать PNG-копии ради цвета.

## Мини-чек-лист

1. Сначала проверить, есть ли нужный типовой символ в Material Icons.
2. Собственный SVG поместить в `Assets/icons/outline` или `Assets/icons/filled`.
3. Проверить `viewBox` и отсутствие внешних ресурсов.
4. Для изменяемого монохромного слоя использовать `currentColor`.
5. Открывать ресурс по `avares://Configurator.Desktop/...`.
6. Рендерить `<svg:Svg Stretch="Uniform" EnableCache="True">`.
7. Передавать цвет через `CurrentColor` как `Color`, а не через `Foreground`/`IBrush`.
8. После добавления иконки проверить ее в светлой и темной теме и в disabled-состоянии.
