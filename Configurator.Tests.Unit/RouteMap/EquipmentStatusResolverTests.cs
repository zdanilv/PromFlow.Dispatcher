using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class EquipmentStatusResolverTests
{
    private static readonly RouteMapPaletteSettings Palette = new();
    private static readonly EquipmentCommandCard Bucket = RouteMapSeed.Create().MapEquipment.Single();
    private static readonly EquipmentStatusMapping Mapping = Bucket.StatusMapping!;
    private static readonly SignalBinding TextBinding = Bucket.Bindings.Single(binding => binding.Role == SignalBindingRole.Text);

    [Theory]
    [InlineData(0, "Выключен", "#77828D", EquipmentStatusCategory.Normal)]
    [InlineData(1, "Ожидание", "#D99B22", EquipmentStatusCategory.Information)]
    [InlineData(2, "Авария", "#D95D4E", EquipmentStatusCategory.Fault)]
    [InlineData(3, "Выполнение", "#3A9D5D", EquipmentStatusCategory.Information)]
    [InlineData(4, "Выгрузка", "#3A9D5D", EquipmentStatusCategory.Information)]
    [InlineData(5, "Загрузка", "#3A9D5D", EquipmentStatusCategory.Information)]
    public void Resolves_seed_entries(ushort code, string text, string color, EquipmentStatusCategory category)
    {
        var status = Resolve(Value(code));

        Assert.Equal(code, status.Code);
        Assert.Equal(text, status.Text);
        Assert.Equal(color, status.Color);
        Assert.Equal(category, status.Category);
        Assert.Equal(EquipmentStatusQuality.Known, status.Quality);
        Assert.True(status.IsTrusted);
        Assert.Equal(category == EquipmentStatusCategory.Fault, status.BlockStart);
        Assert.Equal(category == EquipmentStatusCategory.Fault, status.BlockStop);
        Assert.Contains(TextBinding.SignalId, status.DiagnosticText);
    }

    [Theory]
    [InlineData(true, false, "запрещает ПУСК.")]
    [InlineData(false, true, "запрещает СТОП.")]
    [InlineData(true, true, "запрещает ПУСК и СТОП.")]
    [InlineData(false, false, "Дополнительных запретов")]
    public void Resolves_custom_warning_and_independent_command_blocks(bool blockStart, bool blockStop, string diagnostic)
    {
        var mapping = Mapping with
        {
            Entries = [new(ushort.MaxValue, "Ограничение", "#80123456", EquipmentStatusCategory.Warning, blockStart, blockStop)]
        };
        var signal = Value(ushort.MaxValue, SignalValueType.Word);

        var status = Resolve(signal, mapping: mapping);

        Assert.Equal(ushort.MaxValue, status.Code);
        Assert.Equal("Ограничение", status.Text);
        Assert.Equal("#80123456", status.Color);
        Assert.Equal(EquipmentStatusCategory.Warning, status.Category);
        Assert.Equal(RouteObjectState.Warning, status.StateOverride);
        Assert.Equal(EquipmentStatusQuality.Known, status.Quality);
        Assert.True(status.IsTrusted);
        Assert.Equal(blockStart, status.BlockStart);
        Assert.Equal(blockStop, status.BlockStop);
        Assert.Contains(diagnostic, status.DiagnosticText);
        Assert.Equal(status, Resolve(signal, mapping: mapping));
    }

    [Fact]
    public void Keeps_unknown_code_and_uses_configured_warning_text_and_color()
    {
        var status = Resolve(Value((ushort)9), mapping: Mapping with { UnknownText = "Код не настроен" },
            palette: Palette with { Warning = "#112233" });

        Assert.Equal((ushort)9, status.Code);
        Assert.Equal("Код не настроен (код 9)", status.Text);
        Assert.Equal("#112233", status.Color);
        Assert.Equal(EquipmentStatusCategory.Warning, status.Category);
        Assert.Equal(RouteObjectState.Warning, status.StateOverride);
        AssertBlocked(status, EquipmentStatusQuality.UnknownCode);
    }

    [Fact]
    public void Missing_value_uses_unavailable_text()
    {
        AssertUnavailable(null, EquipmentStatusQuality.Missing, "отсутствует");
    }

    [Theory]
    [InlineData(false, false, "Недостоверное")]
    [InlineData(true, true, "устарело")]
    public void Rejects_bad_or_stale_values(bool isGood, bool isStale, string diagnostic)
    {
        AssertUnavailable(Value((ushort)3) with { IsQualityGood = isGood, IsStale = isStale },
            EquipmentStatusQuality.BadOrStale, diagnostic);
    }

    [Theory]
    [InlineData(null, SignalValueType.UInt16)]
    [InlineData("0", SignalValueType.UInt16)]
    [InlineData(-1, SignalValueType.Word)]
    [InlineData(65536u, SignalValueType.Word)]
    [InlineData(0.0, SignalValueType.UInt16)]
    [InlineData((ushort)0, SignalValueType.Int16)]
    public void Rejects_incompatible_values_and_metadata(object? value, SignalValueType type)
    {
        AssertUnavailable(Value(value, type), EquipmentStatusQuality.WrongType, "Ожидается Text");
    }

    [Fact]
    public void Disconnect_overrides_fault_and_disabled_and_discards_code()
    {
        var status = Resolve(Value((ushort)2), connected: false, globalFault: true, localFault: true, enabled: false);

        Assert.Equal("Не в сети", status.Text);
        Assert.Equal(Palette.Offline, status.Color);
        Assert.Equal(RouteObjectState.Offline, status.StateOverride);
        Assert.Null(status.Code);
        AssertBlocked(status, EquipmentStatusQuality.Disconnected);
    }

    [Theory]
    [InlineData(true, false, "Авария системы")]
    [InlineData(false, true, "Авария оборудования")]
    [InlineData(true, true, "Авария системы")]
    public void External_fault_overrides_running_and_disabled(bool globalFault, bool localFault, string text)
    {
        var status = Resolve(Value((ushort)3), globalFault: globalFault, localFault: localFault, enabled: false);

        Assert.Equal((ushort)3, status.Code);
        Assert.Equal(text, status.Text);
        Assert.Equal(Palette.Fault, status.Color);
        Assert.Equal(EquipmentStatusCategory.Fault, status.Category);
        Assert.Equal(RouteObjectState.Fault, status.StateOverride);
        AssertBlocked(status, EquipmentStatusQuality.ForcedFault);
    }

    [Fact]
    public void Mapped_fault_overrides_disabled_and_blocks_both_even_without_entry_flags()
    {
        var entry = Mapping.Entries.Single(entry => entry.Code == 2);
        var mapping = Mapping with { Entries = [entry with { BlockStart = false, BlockStop = false }] };

        var status = Resolve(Value((ushort)2), enabled: false, mapping: mapping);

        Assert.Equal("Авария", status.Text);
        Assert.Equal(entry.Color, status.Color);
        Assert.Equal(RouteObjectState.Fault, status.StateOverride);
        Assert.True(status.IsTrusted);
        Assert.True(status.BlockStart);
        Assert.True(status.BlockStop);
    }

    [Theory]
    [InlineData(3, "Выполнение", EquipmentStatusQuality.Known)]
    [InlineData(9, "Неизвестный статус (код 9)", EquipmentStatusQuality.UnknownCode)]
    public void Disabled_keeps_label_with_muted_color(ushort code, string text, EquipmentStatusQuality quality)
    {
        var status = Resolve(Value(code), enabled: false);

        Assert.Equal(text, status.Text);
        Assert.Equal(Palette.Disabled, status.Color);
        Assert.Equal(RouteObjectState.Disabled, status.StateOverride);
        Assert.Equal(quality, status.Quality);
        Assert.True(status.BlockStart);
        Assert.True(status.BlockStop);
    }

    private static void AssertUnavailable(SignalValue? value, EquipmentStatusQuality quality, string diagnostic)
    {
        var status = Resolve(value, mapping: Mapping with { UnavailableText = "Нет данных" });

        Assert.Equal("Нет данных", status.Text);
        Assert.Equal(Palette.MutedText, status.Color);
        Assert.Equal(RouteObjectState.Unknown, status.StateOverride);
        Assert.Null(status.Code);
        Assert.Null(status.Category);
        Assert.Contains(diagnostic, status.DiagnosticText);
        AssertBlocked(status, quality);
    }

    private static void AssertBlocked(EquipmentStatusPresentation status, EquipmentStatusQuality quality)
    {
        Assert.Equal(quality, status.Quality);
        Assert.False(status.IsTrusted);
        Assert.True(status.BlockStart);
        Assert.True(status.BlockStop);
    }

    private static EquipmentStatusPresentation Resolve(
        SignalValue? signal,
        bool connected = true,
        bool globalFault = false,
        bool localFault = false,
        bool enabled = true,
        EquipmentStatusMapping? mapping = null,
        RouteMapPaletteSettings? palette = null) =>
        EquipmentStatusResolver.Resolve(mapping ?? Mapping, TextBinding, signal,
            connected, globalFault, localFault, enabled, palette ?? Palette);

    private static SignalValue Value(object? value, SignalValueType type = SignalValueType.UInt16) =>
        new(TextBinding.SignalId, value, type, DateTimeOffset.UnixEpoch, IsQualityGood: true, IsStale: false);
}
