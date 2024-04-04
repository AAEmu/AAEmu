﻿using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class AcceptQuestEffect : EffectTemplate
{
    public uint QuestId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("AcceptQuestEffect");

        // Only allow Characters to start quests
        if (target is not Character character)
        {
            Logger.Debug($"No target character given");
            return;
        }

        // Workaround for older quest types that worked differently, but now all use Skill 11141 on their items
        // Check if caster is a Item
        if (casterObj is SkillItem skillItem)
        {
            var item = ItemManager.Instance.GetItemByItemId(skillItem.ItemId);
            // Is this item a QuestStarted?
            if (item.Template.ImplId == ItemImplEnum.AcceptQuest)
            {
                // Try to find it's actual QuestId
                var itemQuestId = QuestManager.Instance.GetQuestIdFromStarterItemNew(skillItem.ItemTemplateId);
                if (itemQuestId > 0)
                {
                    // Add alternative quest by Id
                    if (!character.Quests.AddQuest(itemQuestId))
                    {
                        Logger.Debug($"Failed to add Quest:{itemQuestId} from Item:{item.TemplateId}, for Player: {character.Name} ({character.Id})");
                        return;
                    }

                    Logger.Debug($"Replaced quest from starter item {item.Id} (template:{item.Template.Id}) to use QuestId {itemQuestId} instead of {QuestId} for player {character.Name}");
                    return;
                }
            }
        }

        // The above workaround didn't yield any results, use the normal QuestId defined for this effect
        character.Quests.AddQuest(QuestId);
    }
}
