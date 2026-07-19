using FpsFrenzy.Content.Pipeline;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;

namespace FpsFrenzy.Content.Tests;

public sealed class AnimationUnitCorrectionTests
{
    [Fact]
    public void UniformHundredToOneFbxBasisMismatchIsNormalized()
    {
        AnimationUnitCorrection correction =
            FpsFrenzySkinnedModelProcessor.CalculateAnimationUnitCorrection(new Vector3(0.01f));

        Assert.True(correction.RequiresCorrection);
        Assert.Equal(0.01f, correction.Scale.X, precision: 5);
        Assert.Equal(0.01f, correction.TranslationScale, precision: 5);
    }

    [Theory]
    [InlineData(1f, 1f, 1f)]
    [InlineData(0.5f, 0.5f, 0.5f)]
    public void OrdinaryScaleIsNotTreatedAsUnitConversion(float x, float y, float z)
    {
        AnimationUnitCorrection correction =
            FpsFrenzySkinnedModelProcessor.CalculateAnimationUnitCorrection(new Vector3(x, y, z));

        Assert.False(correction.RequiresCorrection);
        Assert.Equal(Vector3.One, correction.Scale);
        Assert.Equal(1f, correction.TranslationScale);
    }

    [Theory]
    [InlineData(0.01f, 1f, 1f)]
    [InlineData(-0.01f, -0.01f, -0.01f)]
    public void AmbiguousExtremeBasisMismatchFailsValidation(float x, float y, float z)
    {
        InvalidContentException exception = Assert.Throws<InvalidContentException>(() =>
            FpsFrenzySkinnedModelProcessor.CalculateAnimationUnitCorrection(new Vector3(x, y, z)));

        Assert.Contains("unambiguous uniform unit conversion", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteBasisMismatchFailsValidation()
    {
        InvalidContentException exception = Assert.Throws<InvalidContentException>(() =>
            FpsFrenzySkinnedModelProcessor.CalculateAnimationUnitCorrection(
                new Vector3(float.NaN, 1f, 1f)));

        Assert.Contains("non-finite", exception.Message, StringComparison.Ordinal);
    }
}
