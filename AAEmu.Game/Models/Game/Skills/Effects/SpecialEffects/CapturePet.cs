using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class CapturePet : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.CapturePet;
    
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
        // Only players are allowed to capture pets
        if (caster is not Character player)
            return; 

        Logger.Debug("Special effects: CapturePet value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        // Need to grab the actual target from the buff owner itself, as target will be the player in this case
        BaseUnit targetObject = null;
        if (castObj is CastBuff castBuff)
        {
            targetObject = castBuff.Buff.Owner;
        }
        
        if (targetObject is not Npc targetNpc)
        {
            Logger.Warn($"Special effects: CapturePet {player.Name} tried to capture a non-Npc");
            return;
        }
        
        // TODO: Verify Target Buffs
        // 6675 Capture-able target (tag 1304)
        
        // TODO: Verify Target NPC Grade

        // If valid target
        if (targetNpc.Template.PetItemId > 0)
        {
            var itemTemplate = ItemManager.Instance.GetTemplate(targetNpc.Template.PetItemId);
            if (itemTemplate == null)
            {
                player.SendErrorMessage(ErrorMessageType.BagInvalidItem);
                return;
            }
            // And player can get the item
            if (!player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CapturePet, targetNpc.Template.PetItemId, 1))
            {
                player.SendErrorMessage(ErrorMessageType.BagFull);
                return;
            }

            // Capture (kill) the target
            targetNpc.ReduceCurrentHp(player, targetNpc.MaxHp, KillReason.Capture);
            // Immediately despawn it
            targetNpc.DoDespawn(targetNpc);
        }
        else
        {
            player.SendMessage($"Unable to capture this target yet, take a screenshot of this, and inform the developers. NPC {targetNpc.TemplateId}");
        }
    }
}
