using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncLootItem : DoodadFuncTemplate
    {
        public uint WorldInteractionId { get; set; }
        public uint ItemId { get; set; }
        public int CountMin { get; set; }
        public int CountMax { get; set; }
        public int Percent { get; set; }
        public int RemainTime { get; set; }
        public uint GroupId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncLootItem: skillId {0}, WorldInteractionId {1}, ItemId {2}, CountMin {3}, CountMax {4}, Percent {5}, RemainTime {6}, GroupId {7}",
                skillId, WorldInteractionId, ItemId, CountMin, CountMax, Percent, RemainTime, GroupId);

            _log.Debug("InteractionEffect, {0}", (WorldInteractionType)WorldInteractionId);
            var classType = Type.GetType("AAEmu.Game.Models.Game.World.Interactions." + (WorldInteractionType)WorldInteractionId);
            if (classType == null)
            {
                _log.Error("InteractionEffect, Unknown world interaction: {0}", (WorldInteractionType)WorldInteractionId);
                return;
            }
            _log.Debug("InteractionEffect, Action: {0}", classType); // TODO help to debug...

            var action = (IWorldInteraction)Activator.CreateInstance(classType);
            var casterType = SkillCaster.GetByType((SkillCasterType)SkillCastTargetType.Unit);
            var targetType = SkillCastTarget.GetByType((SkillCastTargetType)SkillCastTargetType.Item);
            action.Execute(caster, casterType, owner, targetType, skillId, ItemId, this);

        }
    }
}
