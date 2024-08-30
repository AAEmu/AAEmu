using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterStatePacket : GamePacket
{
    private readonly Character _character;

    public SCCharacterStatePacket(Character character) : base(SCOffsets.SCCharacterStatePacket, 5)
    {
        _character = character;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_character.InstanceId); // instanceId (iid)
        stream.Write(_character.Guid);       // guid
        stream.Write(0);                     // rwd

        #region Character

        #region Character_B7E0
        _character.Write(stream);            //Character_List_Packet_48B0
        #endregion

        //stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x6E, 0x4E, 0xFD, 0x3E }); //angles
        stream.Write(0f); // anglesX
        stream.Write(0f); // anglesY
        stream.Write(0f); // anglesZ
        stream.Write(_character.Experience);     // exp
        stream.Write(_character.HeirExp);        // uint hierExp add in 3.5.0.3 NA, 5.0, long in 5.7
        stream.Write(_character.RecoverableExp); // recoverableExp
        stream.Write(0u);                        // penaltiedExp
        stream.Write(0u);                        // returnDistrictId
        stream.Write(0);                         // returnDistrict -> type(id)
        stream.Write(0u);                        // resurrectionDistrict -> type(id)

        for (var i = 0; i < 30; i++)             // in 1.2 = 11, in 1.7 = 11, in 3.0+ = 13, 4.5+ = 30
        {
            stream.Write((uint)0);               // abilityExp
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

        stream.Write(0L);               // bountyMoney moneyAmount
        stream.Write(0L);               // bountyTime

        stream.Write(0);                // reportedNo
        stream.Write(0);                // suspectedNo
        stream.Write(0);                // totalPlayTime

        //stream.Write(DateTime.UtcNow);  // createdTime

        stream.Write(_character.ExpandedExpert); // expandedExpert

        stream.Write(DateTime.MinValue); // nationJoinTime
        stream.Write((byte)0);          // remainBotCheckCnt
        stream.Write((short)0);        // failedBotCheckAccumCnt

        for (var i = 0; i < 8; i++)     // 8 in 3.5, 4.5, 5.0, 9 in 5.7
        {
            stream.Write(DateTime.MinValue); // instantTime
        }
        stream.Write(0u);               // dailyLeadershipPoint
        stream.Write(DateTime.MinValue);// lastDailyLeadershipPointTime
        stream.Write(0);                // totalReportBadUser
        stream.Write((byte)0);          // usableAbilSetSlotCount

        #region read_7680
        for (var i = 0; i < 2; i++)
        {
            for (var j = 0; j < 30; j++) // 13 in 3.5, 4.5, 30 in 4.5.1.0, 5.7
            {
                stream.Write(false);   // active
                stream.Write((byte)0); // order
                stream.Write((byte)0); // levelUpStatus
                stream.Write(0u);      // highAbilityExp
            }
        }
        #endregion

        #region _pageInfos 
        var size = 1u; // TODO _pageInfos
        stream.Write(size); // size
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < 5; j++)
            {
                stream.Write(0); // stats
            }
            stream.Write(0u); // applyNormalCount
            stream.Write(0u); // applySpecialCount
        }
        #endregion

        stream.Write(0u);       // _selectPageIndex
        stream.Write(0u);       // _extendMaxStats
        stream.Write(0u);       // _applyExtendCount
        stream.Write(0u);       // type
        stream.Write(0);        // appellationStamp

        // TODO slotInfoList
        size = 0u;
        stream.Write(size); // size
        for (var i = 0; i < size; i++)
        {
            stream.Write(0);       // k
            stream.Write((byte)0); // level
            stream.Write(0);       // exp
        }
        // TODO levelEffectList
        size = 0u;
        stream.Write(size); // size
        for (var i = 0; i < size; i++)
        {
            stream.Write((byte)0);  // equipSlot
            stream.Write((sbyte)0); // level
            stream.Write(0u);       // type
        }
        #endregion

        return stream;
    }
}
