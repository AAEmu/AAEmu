using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Specialty;

public sealed class SpecialtyStockEventCheckTask(
    SpecialtyManager specialtyManager,
    uint triggerId,
    long scheduleGeneration) : Task
{
    public override void Execute()
    {
        specialtyManager.RunStockEventCheck(triggerId, Random.Shared.Next(1000), scheduleGeneration);
    }
}
