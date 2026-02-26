using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.v2.Framework;

/// <summary>
/// NPC gravity — currently a no-op.
/// Z correction happens inside MoveTowards() via MoveAlongSurface + GetHeight fallback.
/// Only moving NPCs get Z correction, which is lightweight and avoids per-tick queries for idle NPCs.
/// </summary>
public static class NpcGravity
{
    public static bool ApplyGravity(Npc npc, TimeSpan delta) => false;
}
