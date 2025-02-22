using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;
using NLog.Fluent;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ItemSocketing : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemSocketing;

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
        // TODO ...
        Logger.Debug("Special effects: ItemSocketing value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Character owner)
        {
            Logger.Error($"Special effects: ItemSocketing caster {caster.Id} is not a character");
            return;
        }

        if (casterObj is not SkillItem gemSkillItem)
        {
            Logger.Error($"Special effects: ItemSocketing casterObj {casterObj} is not a SkillItem");
            return;
        }

        if (targetObj is not SkillCastItemTarget skillTargetItem)
        {
            Logger.Error($"Special effects: ItemSocketing targetObj {targetObj} is not a SkillCastItemTarget");
            return;
        }

        var targetItem = owner.Inventory.GetItemById(skillTargetItem.Id);
        var gemItem = owner.Inventory.GetItemById(gemSkillItem.ItemId);

        if (targetItem is null || gemItem is null)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} or gemItem {gemSkillItem.ItemId} not found");
            return;
        }

        if (targetItem is not EquipItem equipItem)
        {
            Logger.Warn($"Special effects: ItemSocketing targetItem {skillTargetItem.Id} was not a EquipItem");
            return;
        }

        var tasksSocketing = new List<ItemTask>();

        byte result = 0;
        var installed = false;
        if (gemItem.TemplateId != Item.DawnStone)
        {
            // Add LunaGem
            var gemCount = 0u;
            foreach (var gem in equipItem.GemIds)
            {
                if (gem != 0)
                {
                    gemCount++;
                }
            }

            // Roll for Success
            var gemRoll = Rand.Next(0, 10000);
            var gemChance = ItemManager.Instance.GetSocketChance(gemCount); // fetches chances from sqlite3
            // var gemChance = int.MaxValue; //gives 100% success rates

            if (gemRoll < gemChance)
            {
                // Success
                equipItem.GemIds[gemCount] = gemItem.TemplateId;
                result = 1;
            }
            else
            {
                // Failed!
                for (var i = 0; i < equipItem.GemIds.Length; i++)
                {
                    equipItem.GemIds[i] = 0;
                }
            }
            installed = true;
        }
        else
        {
            // DawnStone
            for (var i = 0; i < equipItem.GemIds.Length; i++)
            {
                equipItem.GemIds[i] = 0;
            }
            result = 1;
        }

        tasksSocketing.Add(new ItemUpdate(equipItem));

        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Socketing, tasksSocketing, []));
        owner.SendPacket(new SCItemSocketingLunagemResultPacket(result, equipItem.Id, gemItem.TemplateId, installed));
        owner.BroadcastPacket(new SCSkillEndedPacket(skill.TlId), true);
    }
}
