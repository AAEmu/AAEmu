namespace AAEmu.Game.Models.Game.StreamAoi;

/// <summary>
/// Which mirrored units a client may not have culled or evicted mid-fight.
/// </summary>
/// <remarks>
/// The mirror set is capped and the farthest streamed unit is evicted to make room for a nearer one, and
/// units beyond the soft AOI are culled. Doing that to the unit the player is attacking (or to the one
/// attacking the player) takes the enemy out of the client's world: the next click has no unit to resolve,
/// the cast goes out with no target, and the mob vanishes mid-fight — the "untargetable enemy" in a packed
/// instance, where the capped set is constantly turning over.
/// </remarks>
public static class StreamAoiProtectionRules
{
    /// <summary>An NPC stays streamed while it is the player's target or is fighting the player.</summary>
    public static bool MustKeepStreamed(bool isCurrentTarget, bool hasAggroOnMe) =>
        isCurrentTarget || hasAggroOnMe;
}
