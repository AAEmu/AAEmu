using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Butlers;

public sealed class ButlerHarvestCompletionTask(ButlerHarvestCompletionService service) : Task
{
    public override void Execute() => service.ProcessDueJobs();
}
