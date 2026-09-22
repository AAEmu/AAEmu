using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
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
                // item_accept_quests is the authoritative item -> quest mapping the client reads. It is the
                // only source for 94 of its 814 rows: no QuestActConAcceptItem names those items, so the
                // reverse search below found nothing and the starter item fell through to the effect's own
                // quest_id. On the 683 rows both sources know it also disagrees on 7, where the reverse
                // search can only return whichever start component it walks into first.
                var itemQuestId = ItemUseGameData.Instance.GetQuestIdForItem(skillItem.ItemTemplateId);
                if (itemQuestId == 0 || QuestManager.Instance.GetTemplate(itemQuestId) == null)
                {
                    // 37 rows say quest 0 (hunting-request papers the quest itself consumes) and 18 name a
                    // quest that no longer exists; keep the old reverse search as the fallback for them.
                    itemQuestId = QuestManager.Instance.GetQuestIdFromStarterItemNew(skillItem.ItemTemplateId);
                }

                if (itemQuestId > 0)
                {
                    // Add alternative quest by Id
                    if (!character.Quests.AddQuestFromItem(itemQuestId, skillItem.ItemTemplateId))
                    {
                        Logger.Debug($"Failed to add Quest:{itemQuestId} from Item:{item.TemplateId}, for Player: {character.Name} ({character.Id})");
                        return;
                    }

                    Logger.Debug($"Replaced quest from starter item {item.Id} (template:{item.Template.Id}) to use QuestId {itemQuestId} instead of {QuestId} for player {character.Name}");
                    return;
                }
            }
        }

        // A buff_triggers row fires this effect with its buff as the source (BuffTrigger builds the
        // EffectSource from the buff template); 24 of the 25 quests with a Start
        // QuestActConAcceptBuff start this way and that act wants the buff as the acceptor.
        if (source?.Buff != null)
        {
            character.Quests.AddQuestFromBuff(QuestId, source.Buff.Id);
            return;
        }

        // The above workaround didn't yield any results, use the normal QuestId defined for this effect
        character.Quests.AddQuest(QuestId);
    }
}
