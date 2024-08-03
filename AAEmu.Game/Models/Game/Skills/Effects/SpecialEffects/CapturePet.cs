using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
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

        // TODO: Currently hard-coded since I can't find any table with this information
        // You could add things here if you make new pet items for them, and add the current passive buffs to the NPCs
        var captureItem = 0u;
        switch (targetNpc.TemplateId)
        {
            case 2048: // Giant Queen Bee Lv26
            case 3494: // Queen Bee Lv7
            case 8561: // Queen Bee Lv18
            case 13621: // Queen Bee Lv10
                captureItem = 28483; // Nymphal Queen Bee
                break;
            case 7978: // Windlord
                captureItem = 27203; // Captive Windlord
                break;
            case 8181: // Flamelord
                captureItem = 27204; // Captive Flamelord
                break;
            case 8123: // Farkrag the Wanderer
                captureItem = 27205; // Captive Farkrag the Wanderer
                break;
            case 5985: // Daruda the Watcher
                captureItem = 27207; // Captive Daruda the Watcher
                break;
            case 5984: // Tarian the Grim
                captureItem = 27209; // Captive Tarian the Grim
                break;
            case 6446: // Gatekeeper Harrod (final form)
                captureItem = 27210; // Captive Harrod the Gatekeeper
                break;
        }

        // If valid target
        if (captureItem > 0)
        {
            // And player can get the item
            if (!player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CapturePet, captureItem, 1))
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
