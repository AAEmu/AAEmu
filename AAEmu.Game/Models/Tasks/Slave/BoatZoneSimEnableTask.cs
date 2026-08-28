using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Slave;

/// <summary>
/// Enables ship simulation for a hull after the zone has Created it.
/// Scheduled after Create so the type-4 seed lands on a physical body, then helm-on.
/// </summary>
public class BoatZoneSimEnableTask(Game.Units.Slave slave, uint zoneKey) : Task
{
    public override void Execute()
    {
        SlaveManager.CommitBoatSimEnable(slave, zoneKey);
    }
}
