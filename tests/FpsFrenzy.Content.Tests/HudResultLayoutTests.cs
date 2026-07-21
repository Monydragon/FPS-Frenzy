using FpsFrenzy.Kni.Rendering;

namespace FpsFrenzy.Content.Tests;

public sealed class HudResultLayoutTests
{
    [Fact]
    public void TenUnlocksAndFullBuildStayAboveTheResultsActionPanel()
    {
        string[] unlocks =
        [
            "burst-carbine",
            "scatter-blaster",
            "beam-rifle",
            "plasma-launcher",
            "arc-cannon",
            "accelerated-cycler",
            "close-quarters",
            "longshot",
            "phase-stabilizer",
            "adrenal-circuit",
        ];
        string[] build =
        [
            "pulse-capacitor",
            "burst-synchronizer",
            "tight-choke",
            "beam-heat-sink",
            "plasma-payload",
            "arc-relay",
            "calibrated-cells",
            "reinforced-shell",
            "magnetic-salvage",
        ];

        List<(string Text, Microsoft.Xna.Framework.Color Color)> lines =
            HudRenderer.BuildResultDetailLines(unlocks, build);
        string rendered = string.Join('\n', lines.Select(line => line.Text));

        Assert.True(lines.Count <= HudRenderer.MaximumResultDetailLines);
        Assert.All(lines, line => Assert.True(OxaniumFont.Measure(line.Text, 1).X <= 720f));
        Assert.All(unlocks.Concat(build), id =>
            Assert.Contains(id.Replace('-', ' ').ToUpperInvariant(), rendered, StringComparison.Ordinal));
    }

    [Fact]
    public void UnexpectedlyLargeResultDataIsHardBounded()
    {
        string[] unlocks = Enumerable.Range(0, 30).Select(index => $"unlock-{index}").ToArray();
        string[] build = Enumerable.Range(0, 30).Select(index => $"upgrade-{index}").ToArray();

        List<(string Text, Microsoft.Xna.Framework.Color Color)> lines =
            HudRenderer.BuildResultDetailLines(unlocks, build);

        Assert.Equal(HudRenderer.MaximumResultDetailLines, lines.Count);
        Assert.StartsWith("PLUS ", lines[^1].Text, StringComparison.Ordinal);
    }
}
