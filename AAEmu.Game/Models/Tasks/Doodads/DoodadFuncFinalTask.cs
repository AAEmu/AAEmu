using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

public class DoodadFuncFinalTask : DoodadFuncTask
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly BaseUnit _caster;
    private readonly Doodad _owner;
    private readonly uint _skillId;
    // Unused private int _nextPhase;
    private readonly bool _respawn;
    private readonly int _delay;
    private DateTime? _respawnTime;

    public DoodadFuncFinalTask(BaseUnit caster, Doodad owner, uint skillId, bool respawn, int delay) : base(caster, owner, skillId)
    {
        _caster = caster;
        _owner = owner;
        _skillId = skillId;
        // Unused _nextPhase = (int)owner.FuncGroupId;
        _respawn = respawn;
        _owner = owner;
        _delay = delay;
    }

    public override void Execute()
    {
        if (_caster is Character)
            Logger.Debug("[Doodad] DoodadFuncFinalTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);
        else
            Logger.Trace("[Doodad] DoodadFuncFinalTask: Doodad {0}, TemplateId {1}. Using skill {2} with doodad phase {3}", _owner.ObjId, _owner.TemplateId, _skillId, _owner.FuncGroupId);

        if (_respawn && _owner.Spawner != null)
        {
            if (_respawnTime == null && _owner.FuncTask != null)
            {
                // DoodadSpawner.Despawn() clears _owner.FuncTask; avoid rescheduling via that reference.
                // We reschedule this task instance directly for the second stage.
                _owner.FuncTask = null;
                _owner.Spawner.Despawn(_owner);
                _respawnTime = DateTime.UtcNow;
                TaskManager.Instance.Schedule(this, TimeSpan.FromMilliseconds(_delay));
                return;
            }

            var world = WorldManager.Instance.GetWorld(_owner.Transform.InstanceId);
            //_owner.Spawner.DecreaseCount(_owner);
            _owner.Spawner.Position.WorldId = world?.Id ?? WorldManager.DefaultInstanceId;
            _owner.Spawner.Spawn(0);
        }
        else
        {
            _owner.FuncTask = null;
            if (_owner.Spawner != null)
            {
                _owner.Spawner.Despawn(_owner);
            }
            else
            {
                _owner.Delete();
            }
        }
    }
}
