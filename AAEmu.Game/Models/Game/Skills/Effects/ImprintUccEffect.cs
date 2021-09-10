using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ImprintUccEffect : EffectTemplate
    {
        public uint ItemId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("ImprintUccEffect");
            if (!(target is Character player))
                return;
            if (!(casterObj is SkillItem skillItem))
                return;
            if (!(targetObj is SkillCastItemTarget scit))
                return;
            var sourceItem = ItemManager.Instance.GetItemByItemId(skillItem.ItemId);
            var targetItem = ItemManager.Instance.GetItemByItemId(scit.Id);

            // TODO: Check if items are owned by caster
            
            if ((sourceItem != null) && (targetItem != null))
                UccManager.Instance.ApplyStamp(sourceItem, targetItem);
        }
    }
}
