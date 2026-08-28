using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Slave;

/// <summary>
/// Ends zone simulation and hides the hull after the despawn portal has played.
/// See <see cref="SlaveManager.FinalizeBoatDespawn"/>.
/// </summary>
public class BoatDespawnFinalizeTask(Game.Units.Slave slave) : Task
{
    public override void Execute()
    {
        SlaveManager.FinalizeBoatDespawn(slave);
    }
}
