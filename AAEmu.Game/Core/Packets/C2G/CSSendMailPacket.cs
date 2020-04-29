using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendMailPacket : GamePacket
    {
        public CSSendMailPacket() : base(0x098, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("SendMail");

            var type = stream.ReadByte();
            var receiverCharName = stream.ReadString();
            var unkId = stream.ReadUInt32(); //could be status
            var title = stream.ReadString();
            var text = stream.ReadString(); // TODO max length 1600
            var attachments = stream.ReadByte();
            var moneyAmounts = new int[3];
            for (var i = 0; i < 3; i++)
                moneyAmounts[i] = stream.ReadInt32();
            var extra = stream.ReadInt64();
            var itemSlots = new List<(SlotType slotType, byte slot)>();
            for (var i = 0; i < 10; i++)
            {
                var slotType = stream.ReadByte();
                var slot = stream.ReadByte();
                if (slotType == 0)
                    itemSlots.Add(((byte)0, (byte)0));
                else
                    itemSlots.Add(((SlotType)slotType, slot));
            }

            var doodadObjId = stream.ReadBc();
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad == null) // TODO validation || doodad.Template.GroupId == 6)
                return;

            Connection.ActiveChar.Mails.SendMail(type, receiverCharName, "", title, text, attachments, moneyAmounts, extra, itemSlots);

        }
    }
}
