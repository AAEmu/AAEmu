using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Tasks.Zones;

public sealed class ZoneSchedulePersistenceRetryTask(ZoneConflict zoneConflict) : Task
{
    public override void Execute() => zoneConflict.RetryScheduledStatePersistence();
}
