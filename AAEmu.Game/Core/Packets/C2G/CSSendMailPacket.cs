using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Mails.Static;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendMailPacket : GamePacket
{
    public CSSendMailPacket() : base(CSOffsets.CSSendMailPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("SendMail by {0}", Connection.ActiveChar.Name);

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
                itemSlots.Add((0, 0));
            else
                itemSlots.Add(((SlotType)slotType, slot));
        }

        var doodadObjId = stream.ReadBc();
        var doodad = WorldManager.Instance.GetDoodad(doodadObjId);

        // Validate if we are near a MailBox
        bool mailCheckOK;
        
        if (doodad != null)
        {
            // Cannot rely on doodad GroupID being "Other - Mailboxes (6)", as some of the mailboxes belong to other groups (e.g. "Housing - Furniture").
            // Instead, ensure the doodad in its current state supports opening of the mailbox.
            if (doodad.CurrentFuncs?.Any(func => func.FuncType == "DoodadFuncNaviOpenMailbox") == true)
            {
                var dist = MathUtil.CalculateDistance(Connection.ActiveChar.Transform.World.Position, doodad.Transform.World.Position);
                mailCheckOK = (dist <= 5f); // 5m is kinda generous I guess
            }
            else
            {
                Logger.Warn($"SendMail by {Connection.ActiveChar.Name} invalid - doodad ObjId {doodad.Id} ({doodad.TemplateId}) does not have DoodadFuncNaviOpenMailbox func");
                mailCheckOK = false;
            }
        }
        else
            mailCheckOK = false;

        if (mailCheckOK)
        {
            var mailResult = Connection.ActiveChar.Mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, extra, itemSlots);
            if (mailResult == MailResult.Success)
            {
                Connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailSuccess);
            }
            else
            {
                Connection.SendPacket(new SCMailFailedPacket(mailResult, itemSlots.ToArray(), false));
            }
        }
        else
            Connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailFailMailboxNotFound);
    }
}
