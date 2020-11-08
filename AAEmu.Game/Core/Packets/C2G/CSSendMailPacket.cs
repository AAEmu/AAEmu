using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendMailPacket : GamePacket
    {
        public CSSendMailPacket() : base(0x098, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("SendMail by {0}", Connection.ActiveChar.Name);

            var type = (MailType)stream.ReadByte();
            var receiverCharName = stream.ReadString();
            var unkId = stream.ReadUInt32(); //could be status
            var title = stream.ReadString();
            var text = stream.ReadString(); // TODO max length 1600
            var attachments = stream.ReadByte();
            var money0 = stream.ReadInt32();
            var money1 = stream.ReadInt32();
            var money2 = stream.ReadInt32();
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

            // Validate if we are near a MailBox
            bool mailCheckOK ;
            if (doodad != null)
            {
                // Doodad GroupID 6 is "Other - Mailboxes"
                if (doodad.Template.GroupId == 6)
                {
                    var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Position, doodad.Position);
                    mailCheckOK = (dist <= 5f); // 5m is kinda generous I guess
                }
                else
                    mailCheckOK = false;
            }
            else
                mailCheckOK = false;

            if (mailCheckOK)
                Connection.ActiveChar.Mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots);
            else
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailFailMailboxNotFound);
        }
    }
}
