using CoreVector3 = System.Numerics.Vector3;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace FpsFrenzy.Kni.Rendering;

internal static class KniMath
{
    public static XnaVector3 ToXna(this CoreVector3 value) => new(value.X, value.Y, value.Z);
}
