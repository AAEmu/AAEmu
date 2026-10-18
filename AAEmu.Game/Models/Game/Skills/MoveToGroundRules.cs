using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Destination for <c>move_to_ground</c>. Plot "순간이동" / "텔레포트" events and position-targeted
/// skills pass a synthetic location marker (<see cref="uint.MaxValue"/>); that marker is the XY.
/// When the plot has retargeted away from the marker, <see cref="Skill.CastAim"/> (Use-time aim)
/// is the fallback. Without either the unit stays on its current XY and only Z is snapped.
/// </summary>
public static class MoveToGroundRules
{
    public static (float X, float Y) DestinationXy(BaseUnit mover, BaseUnit target, SavedPosition? castAim = null)
    {
        if (target is { ObjId: uint.MaxValue, Transform: not null })
        {
            var landing = target.Transform.World.Position;
            return (landing.X, landing.Y);
        }

        if (castAim is { } aim)
            return (aim.X, aim.Y);

        var current = mover.Transform.World.Position;
        return (current.X, current.Y);
    }
}
