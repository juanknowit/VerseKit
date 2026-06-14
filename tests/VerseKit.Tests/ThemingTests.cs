using Avalonia;
using Avalonia.Media;
using FluentAssertions;
using VerseKit.App.Theming;
using Xunit;

namespace VerseKit.Tests;

public class AccentPresetTests
{
    [Fact]
    public void All_has_the_eight_presets_with_unique_ids()
    {
        AccentPreset.All.Should().HaveCount(8);
        AccentPreset.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ById_is_case_insensitive()
    {
        AccentPreset.ById("GREEN").Id.Should().Be("green");
    }

    [Fact]
    public void ById_falls_back_to_the_default_for_unknown_ids()
    {
        AccentPreset.ById("does-not-exist").Id.Should().Be(AccentPreset.DefaultId);
        AccentPreset.ById(null).Id.Should().Be(AccentPreset.DefaultId);
    }

    [Fact]
    public void Default_preset_exists()
    {
        AccentPreset.All.Select(p => p.Id).Should().Contain(AccentPreset.DefaultId);
    }

    [Fact]
    public void BackgroundGradient_is_a_diagonal_three_stop_brush()
    {
        var brush = AccentPreset.ById("blue").BackgroundGradient
            .Should().BeOfType<LinearGradientBrush>().Subject;

        brush.StartPoint.Should().Be(new RelativePoint(0, 0, RelativeUnit.Relative));
        brush.EndPoint.Should().Be(new RelativePoint(1, 1, RelativeUnit.Relative));
        brush.GradientStops.Should().HaveCount(3);
        brush.GradientStops.Select(s => s.Offset).Should().BeInAscendingOrder();
    }

    [Fact]
    public void BackgroundGradient_top_stop_is_lighter_than_the_bottom_stop()
    {
        var brush = (LinearGradientBrush)AccentPreset.ById("blue").BackgroundGradient;
        double Luma(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

        Luma(brush.GradientStops[0].Color)
            .Should().BeGreaterThan(Luma(brush.GradientStops[^1].Color));
    }
}

public class BackgroundOptionTests
{
    [Fact]
    public void All_has_glass_theme_white()
    {
        BackgroundOption.All.Select(o => o.Id)
            .Should().BeEquivalentTo(new[] { "acrylic", "theme", "white" });
    }

    [Fact]
    public void Default_is_glass_acrylic()
    {
        BackgroundOption.DefaultId.Should().Be("acrylic");
        BackgroundOption.ById(BackgroundOption.DefaultId).Style.Should().Be(BackgroundStyle.Acrylic);
    }

    [Fact]
    public void ById_falls_back_to_default_for_unknown()
    {
        BackgroundOption.ById("nonsense").Id.Should().Be(BackgroundOption.DefaultId);
        BackgroundOption.ById(null).Id.Should().Be(BackgroundOption.DefaultId);
    }
}
