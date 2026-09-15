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
        // A disposed world keeps every object's ParentWorld, so the flag is what separates "the world is
        // still there" from "the world was torn down inside the delay".
        if (!DeferredDoodadSpawnRules.CanSpawn(Doodad?.IsDeleted ?? true, Doodad?.ParentWorld != null,
                Doodad?.ParentWorld?.IsDisposed ?? true))
        {
            Logger.Trace("Deferred doodad spawn skipped, doodad {0} is gone, has no world, or its world was disposed",
                Doodad?.ObjId ?? 0);
            return;
        }

        Logger.Debug("Deferred doodad spawn for template {0}, ObjId {1}", Doodad.TemplateId, Doodad.ObjId);
        Doodad.Spawn();
    }
}
