using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class CharacterStatePacket : GamePacket
{
    private readonly Character _character;

    public CharacterStatePacket(Character character) : base(SCOffsets.CharacterStatePacket, 5)
    {
        _character = character;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_character.Transform.InstanceId); // instanceId (iid)
        stream.Write(_character.Guid);       // guid
        stream.Write(0);                     // rwd
        //stream.Write(0);                     // srwd

        _character.Write(stream);            //Character_List_Packet_48B0

        stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0 }); //angles
        stream.Write(_character.Experience);     // exp
        stream.Write(0u);                        // heirExp (UInt32 in 3.5 client; heir mechanics not ported)
        stream.Write(_character.RecoverableExp); // recoverableExp
        stream.Write(0u);                        // penaltiedExp
        stream.Write(_character.ReturnDistrictId);        // returnDistrictId
        stream.Write(_character.ReturnDistrictId);        // returnDistrict -> type(id)
        stream.Write(_character.ResurrectionDistrictId);  // resurrectionDistrict -> type(id)

        for (var i = 0; i < 13; i++)             // in 1.2 = 11, in 1.7 = 11, in 3.0.3.0 = 13
        {
            if (i == 0 || !_character.Abilities.Abilities.TryGetValue((AbilityType)i, out var ability))
                stream.Write(0u);                // abilityExp
            else
                stream.Write(ability.Exp);       // abilityExp
        }

        stream.Write(_character.Mails.UnreadMailCount.TotalSent);                // totalSentMail
        stream.Write(_character.Mails.UnreadMailCount.TotalReceived);            // totalMail
        stream.Write(_character.Mails.UnreadMailCount.TotalMiaReceived);         // totalMiaMail
        stream.Write(_character.Mails.UnreadMailCount.TotalCommercialReceived);  // totalCommercialMail
        stream.Write(_character.Mails.UnreadMailCount.UnreadReceived);           // unreadMail
        stream.Write(_character.Mails.UnreadMailCount.UnreadMiaReceived);        // unreadMiaMail
        stream.Write(_character.Mails.UnreadMailCount.UnreadCommercialReceived); // unreadCommercialMail
        stream.Write(_character.NumInventorySlots); // numInvenSlots
        stream.Write(_character.NumBankSlots);      // numBankSlots
        stream.Write(_character.Money);  // moneyAmount - Inventory
        stream.Write(_character.Money2); // moneyAmount - Bank
        stream.Write(0L);                // moneyAmount
        stream.Write(0L);                // moneyAmount

        stream.Write(_character.AutoUseAAPoint); // autoUseAAPoint (д.б. byte)

        stream.Write(0);                // juryPoint
        stream.Write(0);                // jailSeconds

        stream.Write(0L);               // bountyMoney
        stream.Write(0L);               // bountyTime

        stream.Write(0);                // reportedNo
        stream.Write(0);                // suspectedNo
        stream.Write(0);                // totalPlayTime

        stream.Write(DateTime.UtcNow);  // createdTime

        stream.Write(_character.ExpandedExpert);

        stream.Write(0L);               // nationJoinTime
        stream.Write((byte)0);          // remainBotCheckCnt
        stream.Write((short)0);         // failedBotCheckAccumCnt

        for (var i = 0; i < 8; i++)
        {
            stream.Write(0L);           // instantTime
        }
        stream.Write(0u);               // dailyLeadershipPoint
        stream.Write(DateTime.MinValue);// lastDailyLeadershipPointTime
        stream.Write(0);                // totalReportBadUser
        stream.Write((byte)0);          // usableAbilSetSlotCount

        #region read_1EF0
        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 13; j++)
            {
                stream.Write(true);    // active
                stream.Write((byte)0); // order
                stream.Write((byte)0); // levelUpStatus
                stream.Write(0u);      // highAbilityExp
            }
        }
        #endregion

        #region read_1F80
        for (var i = 0; i < 5; i++)
        {
            stream.Write(0);    // stats
        }
        stream.Write(0);        // extendMaxStats
        stream.Write(0);        // applyExtendCount
        stream.Write(0);        // applyNormalCount
        stream.Write(0);        // applySpecialCount
        #endregion
        return stream;
    }
}
