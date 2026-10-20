using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUpdateActionSlotPacket() : GamePacket(CSOffsets.CSUpdateActionSlotPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var slot = stream.ReadByte();
        var type = (ActionSlotType)stream.ReadByte();

        switch (type)
        {
            case ActionSlotType.None:
                var character = Connection.ActiveChar;
                var now = DateTime.UtcNow;
                var keepsShortcut = character.SkillItemShortcutKeeps(slot);
                var usedRecently = character.SkillItemShortcutWasJustUsed(slot, now);
                var movedRecently = character.SkillItemShortcutWasJustMoved(slot, now);
                var alreadyOnAnotherSlot = character.SkillItemShortcutExistsOnOtherSlot(slot);
                if (ActionSlotShortcutRules.ShouldKeepAgainstClear(
                        keepsShortcut, usedRecently, movedRecently, alreadyOnAnotherSlot))
                {
                    // Use-clear or swap-clear: keep the stored shortcut and push it back to the client.
                    character.SendPacket(new SCActionSlotsPacket(character.Slots));
                    break;
                }

                character.RememberSkillItemShortcutClear(slot, keepsShortcut, now);
                character.SetAction(slot, ActionSlotType.None, 0);
                break;
            case ActionSlotType.ItemType:
            case ActionSlotType.Spell:
            case ActionSlotType.RidePetSpell:
                var actionId = stream.ReadUInt32();
                Connection.ActiveChar.SetAction(slot, type, actionId);
                break;
            case ActionSlotType.ItemId:
                var itemId = stream.ReadUInt64();
                Connection.ActiveChar.SetAction(slot, type, itemId);
                break;
            default:
                Logger.Error("UpdateActionSlot, Unknown ActionSlotType!");
                break;
        }
    }
}
