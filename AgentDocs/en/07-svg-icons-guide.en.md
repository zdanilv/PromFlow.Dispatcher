# UI icons in Configurator.Desktop: Material Icons and SVG

## Adopted approach

The application uses two complementary ways to render vector icons:

- `Material.Icons.Avalonia` is the default for standard Material Design UI icons,
  including phone, power, settings, play, and refresh;
- `Svg.Controls.Skia.Avalonia` with embedded SVG files is for branded, bespoke, or
  multicolour artwork that Material Icons does not provide.

Do not export standard Material icons to `Configurator.Desktop/Assets/icons` or create
SVG/PNG copies of them. The package supplies SVG Paths, exposes a strongly typed `Kind`
property, and avoids manual file management. Custom SVG files remain embedded
`AvaloniaResource` items, so they are available in the published app and do not depend
on the current working directory.

The current `Material.Icons.Avalonia` version is `3.0.2`. The authoritative usage
contract and all supported forms are in the [official Getting Started guide](https://github.com/SKProCH/Material.Icons#getting-started).

For a monochrome custom SVG icon, use `<svg:Svg>`, not `<Image>` with `SvgImage`.
`Svg` exposes `CurrentColor`, allowing the same icon to follow the theme, disabled
state, or command state. `SvgImage` is appropriate where an `IImage` is required, but
is not the default for recolourable UI icons.

## Material Icons in Avalonia

### Setup is already complete

`Configurator.Desktop.csproj` already contains:

```xml
<PackageReference Include="Material.Icons.Avalonia" Version="3.0.2" />
```

For version 2.0.0 and later, the package styles must be registered once at application
level. This is already done in `Configurator.Desktop/App.axaml`; do not add
`MaterialIconStyles` again in a view or local resource dictionary:

```xml
<Application xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             ...>
  <Application.Styles>
    ...
    <materialIcons:MaterialIconStyles />
  </Application.Styles>
</Application>
```

In each XAML file that uses an icon, declare the namespace on its root element:

```xml
xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
```

`Kind` selects a value from the strongly typed `MaterialIconKind` enum. Use IDE
completion or the Material Design Icons catalogue; do not pass an SVG path or compose
an icon name through string concatenation.

### Visual control

Use `MaterialIcon` when the icon is a standalone visual. `Foreground` controls its
colour and is inherited from the parent when it is not set. Set its size at the usage
site with `Width` and/or `Height`:

```xml
<materialIcons:MaterialIcon Kind="Phone"
                            Width="18"
                            Height="18"
                            Foreground="#003CA3" />
```

For a theme- or state-dependent colour, inherit the parent button/container's
`Foreground` or provide an `IBrush` or brush resource explicitly:

```xml
<Button Foreground="#FFFFFFFF">
  <materialIcons:MaterialIcon Kind="PowerStandby"
                              Width="20"
                              Height="20" />
</Button>
```

### Icon in `Content`

For `Button.Content`, `ToggleButton.Content`, and similar properties, use the
`MaterialIconExt` markup extension instead of creating an SVG file solely for a button.
Set the extension size with `Size`:

```xml
<Button Content="{materialIcons:MaterialIconExt Kind=TuneVerticalVariant, Size=22}" />
```

When an extension needs a colour other than the container's normal `Foreground`, set
`IconForeground`:

```xml
<Button Content="{materialIcons:MaterialIconExt Kind=PowerStandby,
                 Size=22,
                 IconForeground=#FFFFFFFF}" />
```

Use `MaterialIconTextExt` for a button whose single `Content` combines an icon and a
label:

```xml
<Button Content="{materialIcons:MaterialIconTextExt Kind=Play, Text=Start}" />
```

Do not replace the RouteMap `Start`, `Stop`, or `Reset` text commands and their
contracts with icons without a separate UX decision. This package changes only visual
representation.

### Using an icon as `Image.Source`

`MaterialIcon` implements `IImage`, so the extension can be supplied to `Image.Source`:

```xml
<Image Width="24"
       Height="24"
       Source="{materialIcons:MaterialIconExt Kind=Abacus,
               IconForeground=DeepPink}" />
```

In this form, dimensions belong on `<Image>`; size values on `MaterialIcon` do not
affect the result. The predefined `MaterialIcon` animations are not supported while it
is used as an image source. Use the visual `MaterialIcon` control, rather than
`Image.Source`, when animation is needed.

## Embedded SVG files

All SVG assets are stored in `Configurator.Desktop/Assets/icons` and embedded in the
application as `AvaloniaResource` items. Use them for custom artwork only.

## Storage and packaging

Icons are organized by style:

```text
Configurator.Desktop/
  Assets/icons/
    outline/  # monochrome outline icons
    filled/   # monochrome or multicolour filled icons
```

`Configurator.Desktop.csproj` already contains:

```xml
<AvaloniaResource Include="Assets\icons\**" />
```

New SVG files are included automatically. Do not add `CopyToOutputDirectory`: the UI
must access an embedded resource, not a file located beside the executable.

Use a full resource URI so loading does not depend on the view that contains the
markup:

```text
avares://Configurator.Desktop/Assets/icons/outline/user.svg
```

The character case and path must exactly match the file. This is especially important
for publication on Linux and macOS.

## SVG contract

Each UI icon must have a `viewBox`, contain no external dependency, and be
self-contained. Do not use `script`, external CSS/fonts, network or filesystem links
(`href` to an external file), or raster images inside an icon.

Use `currentColor` in `stroke` for a monochrome outline icon:

```xml
<svg xmlns="http://www.w3.org/2000/svg"
     width="24" height="24" viewBox="0 0 24 24"
     fill="none" stroke="currentColor" stroke-width="2"
     stroke-linecap="round" stroke-linejoin="round">
  <path stroke="none" d="M0 0h24v24H0z" fill="none" />
  <!-- icon geometry -->
</svg>
```

Use `fill="currentColor"` for a monochrome filled icon; it normally does not need a
`stroke`. Do not set fixed `#RRGGBB` values on a `path` that should follow the theme:
`CurrentColor` affects only SVG properties that use `currentColor`.

A multicolour icon is an exception: its constant colours may remain in the SVG. If one
layer must change, give it a unique CSS class and pass CSS through the control's `Css`
property; do not modify the file at runtime.

## `user.svg` example

`Assets/icons/outline/user.svg` already conforms to the outline-icon contract:

- `viewBox="0 0 24 24"` defines geometry independent of rendered size;
- `fill="none"` prevents unwanted fill;
- `stroke="currentColor"` makes line colour configurable;
- line width, caps, and joins are defined once on the root `<svg>`.

Do not replace `stroke="currentColor"` with a fixed colour in the file. That would
create duplicate icon files for normal, hover, disabled, and selected states.

## Using SVG in XAML

Declare the namespace once in the view or base resource dictionary:

```xml
xmlns:svg="using:Avalonia.Svg.Skia"
```

Then render the icon as follows:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         Width="20"
         Height="20"
         Stretch="Uniform"
         CurrentColor="#FF526173"
         EnableCache="True" />
```

Set `Width` and `Height` at the usage site, not in the icon file. Usual sizes are 16,
20, or 24 device-independent pixels. `Stretch="Uniform"` preserves proportions.

Set `EnableCache="True"` for normal static UI icons, so the control does not prepare
the rendering again for every frame.

## Theme colours and states

Declare `Color` values in application or view resources:

```xml
<Application.Resources>
  <Color x:Key="Icon.Foreground">#FF526173</Color>
  <Color x:Key="Icon.OnAccent">#FFFFFFFF</Color>
  <Color x:Key="Icon.Disabled">#FF9CA4AA</Color>
</Application.Resources>
```

Supply the colour to an SVG icon as a dynamic resource:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         Width="20"
         Height="20"
         Stretch="Uniform"
         CurrentColor="{DynamicResource Icon.Foreground}"
         EnableCache="True" />
```

`CurrentColor` is an `Avalonia.Media.Color?`; do not pass an `IBrush` or
`SolidColorBrush` used by `Foreground`. For a ViewModel-computed colour, expose a
`Color` value (for example, `IconColor`) and bind it directly:

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
         CurrentColor="{Binding IconColor}"
         Width="20" Height="20" Stretch="Uniform" EnableCache="True" />
```

A button or container `Foreground` alone does not recolour an external SVG. For a file
that uses `currentColor`, always set `CurrentColor` on `<svg:Svg>` (or inherit it from
an ancestor).

For a contrasting button icon:

```xml
<Button Background="#FF2F80ED">
  <svg:Svg Path="avares://Configurator.Desktop/Assets/icons/outline/user.svg"
           Width="16" Height="16"
           Stretch="Uniform"
           CurrentColor="{DynamicResource Icon.OnAccent}"
           EnableCache="True" />
</Button>
```

## Special cases

To replace a fixed colour, add a class to the relevant SVG layer and supply a CSS rule:

```xml
<path class="icon-accent" d="..." />
```

```xml
<svg:Svg Path="avares://Configurator.Desktop/Assets/icons/filled/example.svg"
         Css=".icon-accent { fill: #FF2F80ED; }"
         Width="20" Height="20" Stretch="Uniform" EnableCache="True" />
```

For a simple monochrome icon, CSS is unnecessary: `currentColor` and `CurrentColor`
are simpler and clearer. Do not load SVG files through absolute filesystem paths,
modify SVG text for each state, or create PNG copies to change colour.

## Checklist

1. Use Material Icons first when the required standard symbol is available.
2. Store only custom SVG assets under `Assets/icons/outline` or `Assets/icons/filled`.
3. Verify `viewBox` and the absence of external resources in a new SVG.
4. Use `currentColor` for a monochrome layer that must be recolourable.
5. Address assets through `avares://Configurator.Desktop/...`.
6. Render SVG using `<svg:Svg Stretch="Uniform" EnableCache="True">` and pass a
   `Color`, not an `IBrush`, through `CurrentColor`.
7. Check every new icon in light and dark themes and in the disabled state.
