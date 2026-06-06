using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.World;

/// <summary>
/// One-shot TaskManager job that re-spawns a downed Halcyona War Golem (template
/// 13796 / 13798) 10 minutes after death. Skips the 5-minute Immobilize phase: the
/// respawned golem walks the path immediately.
/// </summary>
public class HalcyonaGolemRespawnTask(uint spawnerId) : Task
{
    public override void Execute()
    {
        TowerDefManager.Instance.RespawnHalcyonaGolem(spawnerId);
    }
}
