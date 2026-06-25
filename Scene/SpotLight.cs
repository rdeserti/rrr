using rrr.Core;
using rrr.VMath;

namespace rrr.Scene
{
    /// <summary>
    /// A cone of light from <see cref="Position"/> along <see cref="Direction"/>.
    /// Intensity is full within the inner cone and falls off smoothly to zero
    /// at the outer cone. Uses a single perspective shadow map.
    /// </summary>
    public class SpotLight : Light
    {
        public Vector3f Position;

        public Vector3f Direction = new Vector3f(0, -1, 0);

        public ColorRGBAf Color = ColorRGBAf.White;

        public float Intensity = 1.0f;

        /// <summary>Outer cone half-angle (degrees); beyond it, no light.</summary>
        public float ConeAngleDegrees = 45.0f;

        /// <summary>Inner cone half-angle (degrees); within it, full intensity.</summary>
        public float InnerAngleDegrees = 35.0f;

        public SpotLight()
        {
        }
    }
}
