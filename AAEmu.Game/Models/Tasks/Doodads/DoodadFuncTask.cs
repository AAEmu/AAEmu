using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Doodads;

public abstract class DoodadFuncTask : Task
{
    private readonly Doodad _owner;
    private readonly uint _scheduledPhase;

    protected DoodadFuncTask(BaseUnit caster, Doodad owner, uint skillId)
    {
        _owner = owner;
        _scheduledPhase = owner.FuncGroupId;
    }

    /// <summary>
    /// Runs a lifecycle completion only while this task still owns the doodad's scheduled phase.
    /// Phase changes and interactions use the same doodad monitor, closing the dequeue/cancel race.
    /// </summary>
    protected bool ExecuteIfCurrent(Action completion)
    {
        lock (_owner)
        {
            if (Cancelled || _owner.IsDeleted || _owner.Despawn > DateTime.MinValue ||
                !ReferenceEquals(_owner.FuncTask, this) || _owner.FuncGroupId != _scheduledPhase)
                return false;

            completion();
            return true;
        }
    }
}
