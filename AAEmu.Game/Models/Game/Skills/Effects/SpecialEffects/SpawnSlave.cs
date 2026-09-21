using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SpawnSlave : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SpawnSlave;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        // Effects run at cast-end (after SCSkillFired). A cancelled cast must never create a hull —
        // that is what produced overlapping yawls when StopCasting was ignored under ZoneAuthority.
        if (skill is { Cancelled: true })
        {
            Logger.Info("SpawnSlave skipped: skill cancelled tl={0} id={1}", skill.TlId, skill.Id);
            return;
        }

        if (caster is not Character owner)
            return;

        if (casterObj is not SkillItem skillData)
        {
            Logger.Warn("SpawnSlave: caster is not SkillItem for {0}", owner.Name);
            return;
        }

        Logger.Debug(
            "SpawnSlave char={0} item={1} tpl={2} skill={3}",
            owner.Name, skillData.ItemId, skillData.ItemTemplateId, skill?.Id ?? 0);

        float? resolvedX = null;
        float? resolvedY = null;
        float? resolvedZ = null;
        // Skill already ran SetInitialTarget for SummonPos (ObjId MaxValue). Use that world
        // stand so a deck-local ObjId1 basis is not planted near the map origin.
        if (target is { ObjId: uint.MaxValue, Transform: not null })
        {
            var resolved = target.Transform.World.Position;
            if (SlaveSummonSeedRules.HasWorldSeed(resolved.X, resolved.Y, resolved.Z))
            {
                resolvedX = resolved.X;
                resolvedY = resolved.Y;
                resolvedZ = resolved.Z;
            }
        }

        var hasSeed = SlaveSummonSeedRules.TryReadWorldSeed(
            targetObj, resolvedX, resolvedY, resolvedZ, out var seedX, out var seedY, out var seedZ, out var seedYaw);
        var existing = owner.ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(owner.ObjId);
        var sameItem = existing?.SummoningItem != null
                       && skillData.ItemId != 0
                       && existing.SummoningItem.Id == skillData.ItemId;
        var alreadyAtSeed = hasSeed && existing?.Transform != null
                            && SlaveSummonSeedRules.IsAlreadyPlantedAtSeed(
                                existing.Transform.World.Position.X,
                                existing.Transform.World.Position.Y,
                                seedX,
                                seedY);
        if (SlaveSummonSeedRules.ShouldKeepExistingPlant(sameItem, hasSeed, alreadyAtSeed))
        {
            Logger.Info(
                "SpawnSlave kept existing plant obj={0} item={1} seed={2}",
                existing.ObjId, skillData.ItemId, hasSeed);
            return;
        }

        if (!hasSeed)
        {
            owner.ParentWorld.SlaveManager.Create(owner, skillData);
            return;
        }

        using var seed = owner.Transform.CloneDetached();
        SlaveSummonSeedRules.ApplySeed(seed.World, seedX, seedY, seedZ, seedYaw);
        Logger.Info(
            "SpawnSlave seed plant item={0} at ({1:0.0},{2:0.0},{3:0.0}) yaw={4:0.00}",
            skillData.ItemId, seedX, seedY, seedZ, seedYaw);
        owner.ParentWorld.SlaveManager.Create(owner, skillData, hideSpawnEffect: false, seed);
    }
}
