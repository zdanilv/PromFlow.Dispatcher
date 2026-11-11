using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

[Collection("RouteMap Skia rendering")]
public sealed class RouteMapDeadEndVisualTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public Task Seed_renders_labels_roles_and_short_terminal_segments_during_resizes(double dpi) => RunOnUiThread(() =>
    {
        var seed = RouteMapSeed.Create();
        var renderLabels = !string.IsNullOrEmpty(HeadlessTestApplication.ScreenshotDirectory);
        // The standard headless test app uses stub glyphs. In screenshot runs both apps use Skia.
        var definition = renderLabels ? seed : seed with
        {
            Nodes = seed.Nodes.Select(node => node with { Title = string.Empty }).ToArray(),
            Segments = seed.Segments.Select(segment => segment with { Title = null }).ToArray(),
        };
        var map = new RouteMapControl
        {
            Definition = definition,
            NodeRoleStates = definition.Nodes.ToDictionary(node => node.Id,
                node => new RouteNodeRoleState(node.Id, node.IsLoader, node.IsTarget)),
        };
        var window = new Window { WindowDecorations = WindowDecorations.None, Content = map, Width = 800, Height = 600 };
        window.Show();
        try
        {
            window.SetRenderScaling(dpi);
            var nodes = definition.Nodes.ToDictionary(node => node.Id);
            foreach (var size in WindowSizes)
            {
                window.Width = size.Width;
                window.Height = size.Height;
                Dispatcher.UIThread.RunJobs();
                var layout = RouteMapViewportLayout.Create(definition, map.Bounds.Width, map.Bounds.Height);
                var transform = layout.Transform;
                var pixelSize = PixelSize.FromSize(map.Bounds.Size, dpi);
                var renderDpi = new Vector(96 * dpi, 96 * dpi);
                using var frame = new RenderTargetBitmap(pixelSize, renderDpi);
                frame.Render(map);
                using var bitmap = new WriteableBitmap(pixelSize, renderDpi, frame.Format, frame.AlphaFormat);
                using var pixels = bitmap.Lock();
                frame.CopyPixels(pixels);
                AssertPixel(pixels, dpi, new Point(1, 1), Color.Parse("#F7F8F8"));
                foreach (var (id, color) in new[]
                {
                    ("wash", "#68717A"), ("bsu_2", "#68717A"),
                    ("bsu_1", "#D99B22"), ("concrete_bucket", "#3A9D5D"),
                })
                {
                    var node = nodes[id];
                    AssertPixel(pixels, dpi, transform.ToViewPoint(node.X, node.Y), Color.Parse(color));
                }

                foreach (var segment in definition.Segments.Where(line => line.Kind == RouteSegmentKind.DeadEnd))
                {
                    var from = nodes[segment.FromNodeId];
                    var to = nodes[segment.ToNodeId];
                    var style = segment.Style!;
                    Assert.True(RouteSegmentGeometry.TryCreateTerminalViewGeometry(
                        segment, from, to, style, transform, layout.MapViewBounds, out var start, out var end, out var cap));
                    var color = Color.Parse("#68717A");
                    AssertPixel(pixels, dpi, cap.Start + (cap.End - cap.Start) * 0.2, color, tolerance: 55);
                    AssertPixel(pixels, dpi, cap.Start + (cap.End - cap.Start) * 0.8, color, tolerance: 55);

                    var path = RouteSegmentGeometry.Create(segment, start, end);
                    var atStart = segment.TerminalSide == RouteTerminalSide.From;
                    var body = Assert.Single(RouteSegmentGeometry.CalculateTerminalViewDrawableRanges(
                        segment, atStart ? to : from, style, definition.Display!, path.Length, transform.Scale, [], transform.NodeScale));
                    AssertPixel(pixels, dpi, path.PointAt(body.Start + body.Length / 2), color, tolerance: 55);
                    var capBounds = RouteSegmentGeometry.BoundsOf(cap.Start, cap.End).Inflate(style.Thickness * transform.Scale / 2);
                    var label = RouteMapControl.GetTerminalLabelOrigin(capBounds, atStart ? start - end : end - start, 42, 16);
                    var labelBounds = new Rect(label, new Size(48, 18));
                    Assert.True(layout.MapViewBounds.Contains(labelBounds),
                        $"Label for {segment.Id} at {size}: {labelBounds}, viewport {layout.MapViewBounds}.");
                    if (renderLabels)
                        AssertTextInk(pixels, dpi, labelBounds);
                }
                SaveFrame(frame, $"map-{size.Width}x{size.Height}-{dpi * 100:0}");
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public Task Caps_render_at_both_ends_without_clipping_during_resizes(double dpi) =>
        RunOnUiThread(() => AssertCapsDuringResizes(dpi));

    [Theory]
    [InlineData(RouteObjectState.ActiveRoute)]
    [InlineData(RouteObjectState.Fault)]
    [InlineData(RouteObjectState.Offline)]
    public Task Body_keeps_its_state_but_terminal_cap_and_anchor_do_not(RouteObjectState state) =>
        RunOnUiThread(() => AssertBodyState(state));

    [Fact]
    public Task Active_fragment_colors_only_its_part_of_the_body() => RunOnUiThread(AssertActiveFragment);

    [Fact]
    public Task Legend_uses_the_first_terminal_style_and_neutral_t_geometry() => RunOnUiThread(() =>
    {
        var seed = RouteMapSeed.Create();
        var terminal = seed.Segments.First(segment => segment.Kind == RouteSegmentKind.DeadEnd);
        var ordinary = seed.Segments.First(segment => segment.Kind == RouteSegmentKind.Straight);
        var glyph = new RouteMapLegendGlyph
        {
            Kind = RouteMapLegendGlyphKind.DeadEnd,
            Width = 40,
            Height = 30,
            Definition = seed with { Segments = [ordinary, terminal] },
        };
        var window = new Window
        {
            WindowDecorations = WindowDecorations.None,
            Width = 40,
            Height = 30,
            Background = Brushes.White,
            Content = glyph,
        };
        window.Show();
        try
        {
            window.SetRenderScaling(2);
            Dispatcher.UIThread.RunJobs();
            using (var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame()))
            using (var pixels = frame.Lock())
            {
                AssertPixel(pixels, 2, new Point(18, 15), Color.Parse("#68717A"));
                AssertPixel(pixels, 2, new Point(33.75, 8), Color.Parse("#68717A"));
                AssertPixel(pixels, 2, new Point(33.75, 22), Color.Parse("#68717A"));
                AssertPixel(pixels, 2, new Point(18, 8), Colors.White);
            }

            var changed = terminal with
            {
                State = RouteObjectState.ActiveRoute,
                Style = terminal.Style! with { NormalColor = "#235178", Thickness = 8, LineCap = RouteLineCap.Flat },
            };
            glyph.Definition = seed with { Segments = [ordinary, changed] };
            using var editedFrame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            using var editedPixels = editedFrame.Lock();
            var changedColor = Color.Parse("#235178");
            AssertPixel(editedPixels, 2, new Point(18, 18.5), changedColor);
            AssertPixel(editedPixels, 2, new Point(33.75, 8), changedColor);
            AssertPixel(editedPixels, 2, new Point(2, 15), Colors.White);
        }
        finally
        {
            window.Close();
        }
    });

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    private static async Task RunOnUiThread(Action check)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(RouteMapDeadEndVisualTests));
        ExceptionDispatchInfo? failure = null;
        await session.Dispatch(() =>
        {
            try { check(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        }, TestContext.Current.CancellationToken);
        failure?.Throw();
    }

    internal static IReadOnlyList<Size> WindowSizes { get; } =
        [new(800, 600), new(1024, 768), new(1142, 749), new(1280, 720), new(1366, 768), new(1920, 1080), new(800, 600)];

    internal static void SaveFrame(Bitmap frame, string name)
    {
        if (HeadlessTestApplication.ScreenshotDirectory is not { Length: > 0 } directory)
            return;

        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name + ".png"));
    }

    private static void AssertCapsDuringResizes(double dpi)
    {
        var definition = CreateMap();
        var map = new RouteMapControl { Definition = definition };
        var window = new Window { WindowDecorations = WindowDecorations.None, Content = map, Width = 800, Height = 600 };
        window.Show();
        try
        {
            window.SetRenderScaling(dpi);
            foreach (var size in WindowSizes)
            {
                window.Width = size.Width;
                window.Height = size.Height;
                Dispatcher.UIThread.RunJobs();
                Assert.InRange(Math.Abs(size.Width - map.Bounds.Width) * dpi, 0, 1);
                Assert.InRange(Math.Abs(size.Height - map.Bounds.Height) * dpi, 0, 1);

                var transform = RouteMapViewportLayout.Create(definition, map.Bounds.Width, map.Bounds.Height).Transform;
                using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
                using var pixels = frame.Lock();
                Assert.InRange(Math.Abs(size.Width * dpi - pixels.Size.Width), 0, 1);
                Assert.InRange(Math.Abs(size.Height * dpi - pixels.Size.Height), 0, 1);

                var color = Color.Parse(definition.Segments[0].Style!.NormalColor);
                var background = Color.Parse(definition.Display!.Palette.Background);
                var scale = transform.Scale;
                var bottom = transform.ToViewPoint(100, 320);
                var right = transform.ToViewPoint(370, 70);
                // The lower cap is round with an explicit length, and the right cap is flat with the default length.
                AssertCap(pixels, dpi, bottom, new Vector(1, 0), 30 * scale, 4 * scale, 4 * scale, color, background);
                AssertCap(pixels, dpi, right, new Vector(0, 1), 28 * scale, 4 * scale, 0, color, background);
                AssertPixel(pixels, dpi, bottom - new Vector(0, 5 * scale), color);
                AssertPixel(pixels, dpi, right - new Vector(5 * scale, 0), color);
                AssertPixel(pixels, dpi, bottom - new Vector(3 * scale, 10 * scale), color);
                AssertPixel(pixels, dpi, bottom - new Vector(5 * scale, 10 * scale), background);
                AssertPixel(pixels, dpi, right - new Vector(10 * scale, 3 * scale), color);
                AssertPixel(pixels, dpi, right - new Vector(10 * scale, 5 * scale), background);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBodyState(RouteObjectState state)
    {
        var definition = CreateMap();
        var lower = definition.Segments[0];
        var anchor = definition.Nodes[1];
        var map = new RouteMapControl
        {
            Definition = definition,
            HoveredObjectId = anchor.Id,
            SelectedObjectId = anchor.Id,
            AreCommandsEnabled = true,
            RuntimeState = RouteMapRuntimeState.Empty with
            {
                Objects = new Dictionary<string, RouteObjectRuntimeState>
                {
                    [lower.Id] = new(lower.Id, state, null, null, true, false, false),
                    [anchor.Id] = new(anchor.Id, RouteObjectState.ActiveRoute, null, null, true, false, false, IsSignalActive: true),
                },
            },
        };
        var window = new Window { WindowDecorations = WindowDecorations.None, Content = map, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var transform = RouteMapViewportLayout.Create(definition, map.Bounds.Width, map.Bounds.Height).Transform;
            var bottom = transform.ToViewPoint(100, 320);
            var body = transform.ToViewPoint(100, 280);
            var palette = definition.Display!.Palette;
            var style = lower.Style!;
            var bodyColor = Color.Parse(state switch
            {
                RouteObjectState.ActiveRoute => style.ActiveColor,
                RouteObjectState.Fault => palette.Fault,
                _ => palette.Offline,
            });
            var capColor = Color.Parse(style.NormalColor);
            var background = Color.Parse(palette.Background);
            using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            using var pixels = frame.Lock();
            AssertPixel(pixels, 1, body, bodyColor);
            AssertPixel(pixels, 1, bottom - new Vector(0, 6 * transform.Scale), bodyColor);
            AssertPixel(pixels, 1, bottom, capColor);
            AssertPixel(pixels, 1, bottom + new Vector(25 * transform.Scale, 0), capColor);
            AssertPixel(pixels, 1, bottom + new Vector(0, 6 * transform.Scale), background);
            AssertPixel(pixels, 1, bottom + new Vector(0, 14 * transform.Scale), background);

            window.MouseMove(bottom);
            window.MouseDown(bottom, MouseButton.Left);
            window.MouseUp(bottom, MouseButton.Left);
            Assert.Null(map.HoveredObjectId);
            Assert.Null(map.SelectedObjectId);

            var station = transform.ToViewPoint(100, 180);
            window.MouseMove(station);
            window.MouseDown(station, MouseButton.Left);
            window.MouseUp(station, MouseButton.Left);
            Assert.Equal(definition.Nodes[0].Id, map.HoveredObjectId);
            Assert.Equal(definition.Nodes[0].Id, map.SelectedObjectId);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertActiveFragment()
    {
        var definition = CreateMap();
        var activeRoute = new SignalBinding(SignalBindingRole.ActiveRoute, "route.lower.active", SignalBindingDirection.Read, SignalValueType.Bool);
        var first = activeRoute with { Role = SignalBindingRole.ActiveRouteFragment, SignalId = "route.lower.fragment_1.active" };
        var second = first with { SignalId = "route.lower.fragment_2.active" };
        var style = definition.Segments[0].Style! with { FragmentLength = 40, FragmentGap = 24 };
        var lower = definition.Segments[0] with
        {
            Style = style,
            Bindings = [activeRoute],
            ActiveFragments = [new(1, first), new(2, second)],
        };
        definition = definition with { Segments = [lower, definition.Segments[1]] };
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            [activeRoute.SignalId] = new(activeRoute.SignalId, false, SignalValueType.Bool, now, true, false),
            [first.SignalId] = new(first.SignalId, false, SignalValueType.Bool, now, true, false),
            [second.SignalId] = new(second.SignalId, true, SignalValueType.Bool, now, true, false),
        };
        var map = new RouteMapControl { Definition = definition, RuntimeState = new RouteMapRuntimeMapper(definition).Map(signals) };
        var window = new Window { WindowDecorations = WindowDecorations.None, Content = map, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var transform = RouteMapViewportLayout.Create(definition, map.Bounds.Width, map.Bounds.Height).Transform;
            using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            using var pixels = frame.Lock();
            AssertPixel(pixels, 1, transform.ToViewPoint(100, 220), Color.Parse(style.NormalColor));
            AssertPixel(pixels, 1, transform.ToViewPoint(100, 248), Color.Parse(definition.Display!.Palette.Background));
            AssertPixel(pixels, 1, transform.ToViewPoint(100, 280), Color.Parse(style.ActiveColor));
            AssertPixel(pixels, 1, transform.ToViewPoint(100, 320), Color.Parse(style.NormalColor));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertCap(
        ILockedFramebuffer pixels, double dpi, Point center, Vector tangent,
        double halfLength, double halfStroke, double endExtension, Color color, Color background)
    {
        var normal = new Vector(-tangent.Y, tangent.X);
        foreach (var side in new[] { -1, 1 })
        {
            var arm = center + tangent * (side * halfLength * 0.75);
            AssertPixel(pixels, dpi, arm + normal * (halfStroke - 2 / dpi), color);
            AssertPixel(pixels, dpi, arm + normal * (halfStroke + 2 / dpi), background);
            var tip = halfLength + endExtension;
            AssertPixel(pixels, dpi, center + tangent * (side * (tip - 2 / dpi)), color);
            AssertPixel(pixels, dpi, center + tangent * (side * (tip + 2 / dpi)), background);
        }
    }

    private static void AssertTextInk(ILockedFramebuffer pixels, double dpi, Rect bounds)
    {
        var x = (int)Math.Ceiling(bounds.Left * dpi);
        var y = (int)Math.Ceiling(bounds.Top * dpi);
        for (var row = y; row < bounds.Bottom * dpi; row++)
        {
            for (var column = x; column < bounds.Right * dpi; column++)
            {
                if (ReadPixel(pixels, column, row).R < 180)
                    return;
            }
        }
        Assert.Fail($"No text was rendered in {bounds} at {dpi} DPI scale.");
    }

    private static void AssertPixel(ILockedFramebuffer pixels, double dpi, Point point, Color expected, int tolerance = 0)
    {
        var x = (int)Math.Floor(point.X * dpi);
        var y = (int)Math.Floor(point.Y * dpi);
        var color = ReadPixel(pixels, x, y);
        Assert.True(Math.Abs(expected.R - color.R) <= tolerance &&
                    Math.Abs(expected.G - color.G) <= tolerance &&
                    Math.Abs(expected.B - color.B) <= tolerance,
            $"Pixel {x}, {y} at {dpi} DPI scale: expected {expected}, got {color}.");
    }

    private static Color ReadPixel(ILockedFramebuffer pixels, int x, int y)
    {
        Assert.InRange(x, 0, pixels.Size.Width - 1);
        Assert.InRange(y, 0, pixels.Size.Height - 1);
        var index = y * pixels.RowBytes + x * 4;
        var isBgra = pixels.Format == PixelFormat.Bgra8888;
        Assert.True(isBgra || pixels.Format == PixelFormat.Rgba8888);
        return Color.FromRgb(
            Marshal.ReadByte(pixels.Address, index + (isBgra ? 2 : 0)),
            Marshal.ReadByte(pixels.Address, index + 1),
            Marshal.ReadByte(pixels.Address, index + (isBgra ? 0 : 2)));
    }

    private static RouteMapDefinition CreateMap()
    {
        var lowerStation = new RouteNode("lower-station", "", 100, 180, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var lowerAnchor = lowerStation with { Id = "lower-anchor", Y = 320, Kind = RouteNodeKind.TerminalAnchor, MenuKind = RouteNodeMenuKind.SendAndReturn };
        var rightStation = lowerStation with { Id = "right-station", X = 320, Y = 70 };
        var rightAnchor = rightStation with { Id = "right-anchor", X = 370, Kind = RouteNodeKind.TerminalAnchor };
        var style = new RouteSegmentStyle { NormalColor = "#316254", ActiveColor = "#193DAF", Thickness = 8, ActiveThickness = 16 };
        var lower = new RouteSegment("lower", lowerStation.Id, lowerAnchor.Id, RouteObjectState.Idle, false, [], RouteSegmentKind.DeadEnd, Style: style)
        {
            TerminalSide = RouteTerminalSide.To,
            TerminalCapLength = 60,
        };
        var right = lower with
        {
            Id = "right", FromNodeId = rightAnchor.Id, ToNodeId = rightStation.Id,
            TerminalSide = RouteTerminalSide.From, TerminalCapLength = null, Style = style with { LineCap = RouteLineCap.Flat },
        };
        return new RouteMapDefinition(440, 330, [], [lowerStation, lowerAnchor, rightStation, rightAnchor], [lower, right],
            [], [], [], [], new RouteMapDisplaySettings { MapPadding = 10 });
    }
}

[CollectionDefinition("RouteMap Skia rendering", DisableParallelization = true)]
public sealed class RouteMapSkiaRenderingCollection;
