using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Tasks.World;

/// <summary>
/// 80-minute (4800s) auto-despawn for Halcyona War flag doodads (central neutral flag 7035 +
/// faction flag poles 7034/7094 + any other static-state OG doodads tracked on the runner).
/// Fires only on the in-memory <see cref="Doodad"/> reference: safe-no-op if the doodad was
/// already torn down by the phase chain (e.g. flag picked up → cycles back to invisible) or
/// by <c>Stop()</c>'s force-clean.
/// </summary>
public class HalcyonaFlagDespawnTask(Doodad doodad) : Task
{
    public override void Execute()
    {
        try { doodad?.Delete(); }
        catch { /* best-effort — doodad may already be gone */ }
    }
}
