using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSendMailPacket() : GamePacket(CSOffsets.CSSendMailPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        Logger.Debug($"SendMail by {character.Name}");

        // Wire layout from the 10.0.2 client: the sender builds CSSendMailPacket (opcode 0xDB,
        // part_26025.c) from the struct read by FUN_39bdeb70 with the group-mail tail appended by
        // FUN_39a965d0 and the whole packet read back by FUN_39ac17e0 (part_38658.c):
        //   u8 type, str receiverCharName (cap 128), u64 receiverRefId, str title (cap 0x4b0),
        //   str text (cap 0x640), u8 attachments, u64 money x3, u32 money3, u64 extra,
        //   bool groupMail, 10 x (u8 slotType, u8 slot), u32 doodadId (Bc),
        //   u64 groupMoney, u32 userCount, u64 userList[userCount, max 100].
        //
        // The money widths matter: the three main amounts are u64 on the wire (same helpers as
        // the S2C mail-body money fields), followed by a fourth u32 amount. Reading them as i32
        // shifted every later field and made the mailbox doodad check fail for every send.
        var type = (MailType)stream.ReadByte();
        var receiverCharName = stream.ReadString();
        var receiverRefId = stream.ReadUInt64();
        var title = stream.ReadString();
        var text = stream.ReadString();
        var attachments = stream.ReadByte();
        var money0 = stream.ReadUInt64();
        var money1 = stream.ReadUInt64();
        var money2 = stream.ReadUInt64();
        var money3 = stream.ReadUInt32();
        var extra = stream.ReadInt64();
        var groupMail = stream.ReadBoolean();
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
        var groupMoney = stream.ReadUInt64();
        var userCount = stream.ReadUInt32();
        var userList = new List<ulong>();
        for (var i = 0; i < userCount && i < 100; i++)
            userList.Add(stream.ReadUInt64());

        Logger.Debug($"SendMail by {character.Name} to {receiverCharName} (ref {receiverRefId}), group={groupMail}, extraUsers={userList.Count}, groupMoney={groupMoney}");

        if (character.Level + character.HeirLevel < AppConfiguration.Instance.LevelRestrictions.MailLevel)
        {
            character.SendErrorMessage(ErrorMessageType.MailCannotSendSinceLevelLow);
            return;
        }

        var doodad = character.ParentWorld.GetDoodad(doodadObjId);

        // Validate if we are near a MailBox
        bool mailCheckOK;

        if (doodad != null)
        {
            // Cannot rely on doodad GroupID being "Other - Mailboxes (6)", as some of the mailboxes belong to other groups (e.g. "Housing - Furniture").
            // Instead, ensure the doodad in its current state supports opening of the mailbox.
            if (doodad.CurrentFuncs?.Any(func => func.FuncType == "DoodadFuncNaviOpenMailbox") == true)
            {
                var dist = MathUtil.CalculateDistance(character.Transform.World.Position, doodad.Transform.World.Position);
                mailCheckOK = dist <= 5f; // 5m is kinda generous I guess
            }
            else
            {
                Logger.Warn($"SendMail by {character.Name} invalid - doodad ObjId {doodad.Id} ({doodad.TemplateId}) does not have DoodadFuncNaviOpenMailbox func");
                mailCheckOK = false;
            }
        }
        else
            mailCheckOK = false;

        if (mailCheckOK)
        {
            var mailResult = character.Mails.SendMailToPlayer(type, receiverCharName, title, text, attachments, money0, money1, money2, money3, extra, itemSlots, groupMail, userList);
            if (mailResult == MailResult.Success)
            {
                character.SendErrorMessage(ErrorMessageType.MailSuccess);
            }
            else
            {
                Connection.SendPacket(new SCMailFailedPacket(mailResult, itemSlots.ToArray(), false));
            }
        }
        else
            character.SendErrorMessage(ErrorMessageType.MailFailMailboxNotFound);
    }
}
