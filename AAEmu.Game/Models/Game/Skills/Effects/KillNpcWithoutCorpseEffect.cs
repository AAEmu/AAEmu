using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class KillNpcWithoutCorpseEffect : EffectTemplate
    {
        public uint NpcId { get; set; }
        public float Radius { get; set; }
        public bool GiveExp { get; set; }
        public bool Vanish { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Trace("KillNpcWithoutCorpseEffect");

            if (Vanish && Radius == 0)
            {
                // Fixed: "Trainer Daru" disappears after selling a bear
                caster.Buffs.RemoveAllEffects();
                caster.Delete();
            }
            else
            {
                var npcs = WorldManager.Instance.GetAround<Npc>(target, Radius);
                foreach (var npc in npcs.Where(npc => npc.TemplateId == NpcId))
                {
                    npc.Buffs.RemoveAllEffects();
                    npc.Delete();
                }
            }

            //// TODO added for quest Id=1340
            //// find the item that was used for Buff and check it in the quests
            //var goodBuffs = new List<Buff>();
            //var badBuffs = new List<Buff>();
            //var hiddenBuffs = new List<Buff>();
            //caster.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs);

            //foreach (var buff in hiddenBuffs)
            //{
            //    if (buff.Caster is not Character character) { continue; }
            //    var item = (SkillItem)buff.SkillCaster;
            //    character.Inventory.Bag.GetAllItemsByTemplate(item.ItemTemplateId, -1, out var items, out var count);
            //    if (count > 0)
            //    {
            //        character.Quests.OnItemUse(items[0]);
            //    }
            //}

            // TODO added for quest Id=1340
            // find the item that was used for Buff and check it in the quests
            if (caster is not Character character) { return; }
            if (castObj is not CastBuff castBuff) { return; }
            if (castBuff.Buff.SkillCaster is not SkillItem skillItem) { return; }
            var item = character.Inventory.GetItemById(skillItem.ItemTemplateId);
            if (item.Count > 0)
            {
                character.Quests.OnItemUse(item);
            }
        }
    }
}
