using System;
using System.Threading.Tasks;

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

        var character = target as Character;
        if (character == null)
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
                    // Add alternative quest Id
                    if (!character.Quests.Add(itemQuestId))
                        return;
                    // Immediately add the source using a OnItemGather event
                    //character.Quests.OnItemGather(item, item.Count);
                    // инициируем событие
                    Task.Run(() => QuestManager.Instance.DoAcquiredEvents(character, item.TemplateId, item.Count));

                    Logger.Debug($"Replaced quest from starter item {item.Id} (template:{item.Template.Id}) to use QuestId {itemQuestId} instead of {QuestId} for player {character.Name}");
                    return;
                }
            }
        }

        // The above workaround didn't yield any results, use the normal QuestId defined for this effect
        character.Quests.Add(QuestId);
    }
}
