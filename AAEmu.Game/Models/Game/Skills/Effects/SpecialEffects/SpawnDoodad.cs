using System;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

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

        var doodad = DoodadManager.Instance.Create(0, (uint)doodadId, caster, true);
        if (doodad != null)
        {
            doodad.Transform = caster.Transform.CloneDetached(doodad);
            var rpy = target.Transform.World.ToRollPitchYawDegrees();
            switch (skill?.Template.TargetSelection ?? 0)
            {
                case SkillTargetSelection.Source:
                    doodad.Transform = caster.Transform.CloneDetached(doodad);
                    break;
                case SkillTargetSelection.Target:
                    doodad.Transform = target.Transform.CloneDetached(doodad);
                    break;
                case SkillTargetSelection.Line:
                case SkillTargetSelection.Location:
                default:
                    doodad.Transform = caster.Transform.CloneDetached(doodad);
                    break;
            }

            var (xx, yy) = MathUtil.AddDistanceToFrontDeg(1f, doodad.Transform.World.Position.X,
                doodad.Transform.World.Position.Y, rpy.Z + 90f); //  + 90f to Front
            doodad.SetPosition(xx, yy, WorldManager.Instance.GetHeight(doodad.Transform), rpy.X, rpy.Y, rpy.Z);
            doodad.InitDoodad();
            if (delay > 0)
                Thread.Sleep(delay);
            doodad.Spawn();
        }
    }
}
