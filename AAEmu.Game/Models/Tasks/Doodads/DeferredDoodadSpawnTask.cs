using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;

using NLog;

namespace AAEmu.Game.Models.Tasks.Doodads;

/// <summary>
/// Makes a SpawnDoodad special effect's doodad visible after its delay, without holding the effect
/// thread. <c>SpawnDoodad.Execute</c> has already created, placed and initialised the doodad; this is
/// the part that used to sit behind a blocking sleep for the delay.
/// </summary>
public sealed class DeferredDoodadSpawnTask(Doodad doodad) : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Doodad Doodad { get; } = doodad;

    public override void Execute()
    {
        if (!DeferredDoodadSpawnRules.CanSpawn(Doodad?.IsDeleted ?? true, Doodad?.ParentWorld != null))
        {
            Logger.Trace("Deferred doodad spawn skipped, doodad {0} is gone or has no world", Doodad?.ObjId ?? 0);
            return;
        }

        Logger.Debug("Deferred doodad spawn for template {0}, ObjId {1}", Doodad.TemplateId, Doodad.ObjId);
        Doodad.Spawn();
    }
}
