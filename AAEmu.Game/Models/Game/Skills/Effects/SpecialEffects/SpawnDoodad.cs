using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SpawnDoodad : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SpawnDoodad;

    /// <summary>
    /// Spawns a doodad
    /// </summary>
    /// <param name="caster">Original caster</param>
    /// <param name="casterObj">Skill caster object</param>
    /// <param name="target">Skill cast target BaseUnit</param>
    /// <param name="targetObj">Skill cast target object</param>
    /// <param name="castObj">Cast action</param>
    /// <param name="skill">Original skill (if any)</param>
    /// <param name="skillObject">Skill object</param>
    /// <param name="time">Start time</param>
    /// <param name="doodadId">Doodad templateId</param>
    /// <param name="delay">Delay before the doodad becomes "active"?</param>
    /// <param name="createTradePack">Set to one for trade packs created for quests</param>
    /// <param name="value4">Unused</param>
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int doodadId,
        int delay,
        int createTradePack,
        int value4
    )
    {
        if (caster is null)
        {
            Logger.Warn($"Special effects: SpawnDoodad has no caster defined, doodadId {doodadId}, delay {delay}, createTradePack {createTradePack}, value4 {value4}");
            return;
        }

        if (caster is Character)
        {
            Logger.Debug($"Special effects: SpawnDoodad doodadId {doodadId}, delay {delay}, createTradePack {createTradePack}, value4 {value4}");
        }

        var doodad = DoodadManager.Instance.Create(caster.ParentWorld, 0, (uint)doodadId, caster, true);

        // The plot target is the stand (an area location already sits on the cuboid). Copying that
        // transform keeps its radian yaw. The old path rewrote yaw with degree values and, for a
        // self-cast, snapped the doodad back onto the caster, so a corridor decal pointed off the wave.
        var anchor = SpawnDoodadPlacementRules.Anchor(caster, target);
        doodad.Transform = anchor.Transform.CloneDetached(doodad);
        if (SpawnDoodadPlacementRules.NudgeOffCaster(caster, target))
            doodad.Transform.Local.AddDistanceToFront(SpawnDoodadPlacementRules.CasterClearanceMetres);
        var zz = doodad.ParentWorld.Template.GeoData.GetHeight(doodad.Transform.World.Position);
        doodad.Transform.Local.SetHeight(zz);
        doodad.InitDoodad();
        if (DeferredDoodadSpawnRules.ShouldDeferSpawn(delay))
        {
            // Everything above is done; only making the doodad visible waits for the delay. This used
            // to block the effect thread for the whole delay, which pushed the cast's EndSkill back
            // with it, so the spawn is scheduled instead.
            TaskManager.Instance.Schedule(new DeferredDoodadSpawnTask(doodad), TimeSpan.FromMilliseconds(delay));
        }
        else
        {
            doodad.Spawn();
        }

        skill?.RegisterCastStartTelegraph(doodad);
    }
}
