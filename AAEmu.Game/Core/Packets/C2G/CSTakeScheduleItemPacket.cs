using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTakeScheduleItemPacket : GamePacket
{
    public CSTakeScheduleItemPacket() : base(CSOffsets.CSTakeScheduleItemPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSTakeScheduleItemPacket");
        var itemTemplateId = stream.ReadUInt32();

        #region ReceivedGift

        var character = Connection.ActiveChar;
        // immediately change the icon
        character.SendPacket(new SCScheduleItemSentPacket(itemTemplateId, false));
        
        foreach (var item in character.ScheduleItems)
        {
            if (item.ScheduleItemId != itemTemplateId) { continue; }

            var scheduleItem = GameScheduleManager.Instance.GetScheduleItem(itemTemplateId);
            var templateId = scheduleItem.ItemId;
            var itemCount = scheduleItem.ItemCount;
            var giveMax = scheduleItem.GiveMax;

            item.Cumulated = 0;
            item.Gave++;

            if (item.Gave == giveMax)
            {
                Logger.Warn($"TakeScheduleItem: {character.Name}:{character.Id} didn’t receive a gift, already it to the max.");
            }
            else
            {
                character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.TakeScheduleItem, templateId, (int)itemCount);
                Logger.Warn($"TakeScheduleItem: {character.Name}:{character.Id} received a gift");
            }

            // Update Account Divine Clock time
            AccountManager.Instance.UpdateDivineClock(character.AccountId, item.ScheduleItemId, item.Cumulated, item.Gave);
        }

        #endregion
    }
}
