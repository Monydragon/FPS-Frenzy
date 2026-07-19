using FpsFrenzy.Content.Pipeline;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content.Pipeline;

namespace FpsFrenzy.Content.Tests;

public sealed class SkinExcursionValidationTests
{
    [Fact]
    public void OrdinaryAttackPoseRemainsInsideCalibratedExcursionLimit()
    {
        GeometryBounds calibration = new(new Vector3(-1f, 0f, -0.5f), new Vector3(1f, 2f, 0.5f));
        GeometryBounds attack = new(new Vector3(-1.4f, -0.05f, -0.7f), new Vector3(1.5f, 2.4f, 0.8f));

        FpsFrenzySkinnedModelProcessor.ValidateSkinExcursionBounds(
            calibration,
            attack,
            maximumMultiplier: 4f,
            "Attack pose");
    }

    [Fact]
    public void ExplodingGeometryFailsContentValidation()
    {
        GeometryBounds calibration = new(new Vector3(-1f, 0f, -0.5f), new Vector3(1f, 2f, 0.5f));
        GeometryBounds exploded = new(new Vector3(-200f), new Vector3(200f));

        InvalidContentException exception = Assert.Throws<InvalidContentException>(() =>
            FpsFrenzySkinnedModelProcessor.ValidateSkinExcursionBounds(
                calibration,
                exploded,
                maximumMultiplier: 4f,
                "Broken pose"));

        Assert.Contains("outside the calibrated presentation bounds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RootMotionPolicyLocksOnlyTheConfiguredRootTranslation()
    {
        Vector3 sampled = new(4f, 0.5f, -3f);
        Vector3 bind = new(0f, 0.25f, 0f);

        Assert.Equal(bind, FpsFrenzySkinnedModelProcessor.ResolveRootTranslation(sampled, bind, true));
        Assert.Equal(sampled, FpsFrenzySkinnedModelProcessor.ResolveRootTranslation(sampled, bind, false));
    }
}
