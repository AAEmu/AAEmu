using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Expeditions;

public sealed class ExpeditionInactiveOwnerTask : Task
{
    public override void Execute() => ExpeditionManager.Instance.EvaluateInactiveOwners();
}
