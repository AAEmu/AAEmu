using System.Numerics;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Knockback is a physics impulse on the wire, not a continent landing plant. The three floats of
/// <c>SCKnockBackUnit</c> are handed to the client's actor physics (mass-scaled). The server keeps
/// combat/AOI honest with a short XY slide derived from the authored horizontal magnitude — it does
/// not open a crowd-control movement bypass for client-reported settles.
///
/// <c>special_effects</c> type 13: <c>value1</c> is the horizontal impulse magnitude in the
/// client's impulse units (the client's own internal nudges use 100; shipped rows run roughly
/// 10..1400), <c>value2</c> is the vertical impulse on the same scale (commonly 0..90, also
/// negative). They are independent axes — not a polar elevation angle. Treating value2 as
/// degrees and taking sin(value2)*value1 for Z was launching players into the sky on dig rows
/// (850/45 → Z 601 instead of Z 45). <c>value3</c>/<c>value4</c> are zero on every shipped row.
/// Horizontal direction is away from the caster on the XY plane.
/// </summary>
public static class KnockBackRules
{
    /// <summary>
    /// Server-authority XY slide so range/AOI track the shove without trusting a client move under
    /// stun/root. Metres come from the historical distance heuristic on the authored horizontal
    /// magnitude (values above 100 are thousandths of a metre). Z is left to client physics.
    /// </summary>
    public static bool TryServerAuthoritySlide(Vector3 impulse, int horizontalMagnitude, out Vector3 delta)
    {
        var hLen = MathF.Sqrt(impulse.X * impulse.X + impulse.Y * impulse.Y);
        if (hLen < 0.001f)
        {
            delta = Vector3.Zero;
            return false;
        }

        var abs = Math.Abs(horizontalMagnitude);
        var metres = abs > 100 ? abs / 1000f : abs;
        if (metres <= 0f)
        {
            delta = Vector3.Zero;
            return false;
        }

        delta = new Vector3(impulse.X / hLen * metres, impulse.Y / hLen * metres, 0f);
        return true;
    }

    /// <summary>
    /// Impulse vector for a target pushed away from <paramref name="caster"/>. Returns false when the
    /// two stand on the same spot so the caller can fall back to the caster's facing.
    /// </summary>
    public static bool TryImpulse(Vector3 caster, Vector3 target, int horizontalMagnitude, int verticalImpulse,
        out Vector3 impulse)
    {
        var dx = target.X - caster.X;
        var dy = target.Y - caster.Y;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.001f)
        {
            impulse = Vector3.Zero;
            return false;
        }

        impulse = ImpulseAlong(dx / len, dy / len, horizontalMagnitude, verticalImpulse);
        return true;
    }

    /// <summary>
    /// Impulse along a unit XY direction: full <paramref name="horizontalMagnitude"/> on XY,
    /// <paramref name="verticalImpulse"/> on Z (independent, same units).
    /// </summary>
    public static Vector3 ImpulseAlong(float dirX, float dirY, int horizontalMagnitude, int verticalImpulse)
        => new(dirX * horizontalMagnitude, dirY * horizontalMagnitude, verticalImpulse);

    /// <summary>Unit XY direction from a yaw in radians (used when caster and target overlap).</summary>
    public static (float x, float y) DirectionFromYaw(float yawRadians)
        => (MathF.Cos(yawRadians), MathF.Sin(yawRadians));
}
