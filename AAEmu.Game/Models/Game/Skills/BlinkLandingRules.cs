using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Landing math for the Blink special effect. <c>value1</c> is the travel distance in metres and
/// <c>value2</c> rotates the direction (±90° for the side-step and mirror blinks). The rotation used to
/// be dropped server-side, so every blink was applied straight ahead while the client showed the
/// sideways one and the resulting desync was corrected back by the zone.
/// </summary>
public static class BlinkLandingRules
{
    public static (float X, float Y) GetLanding(float x, float y, float casterYawDegrees, int distanceMetres, int offsetDegrees)
        => MathUtil.AddDistanceToFrontDeg(distanceMetres, x, y, casterYawDegrees + offsetDegrees);
}
