using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
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

            _character.Write(stream);            //Character_List_Packet_48B0

            stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0 }); //angles
            stream.Write(_character.Expirience);     // exp
            stream.Write(_character.RecoverableExp); // recoverableExp
            stream.Write(0u);                        // penaltiedExp
            stream.Write(0u);                        // returnDistrictId
            stream.Write(0);                         // returnDistrict -> type(id)
            stream.Write(0u);                        // resurrectionDistrict -> type(id)

            for (var i = 0; i < 13; i++)             // in 1.2 = 11, in 1.7 = 11, in 3.0.3.0 = 13
            {
                stream.Write((uint)0);               // abilityExp
            }

            stream.Write(_character.Mails.unreadMailCount.TotalSent);                // totalSentMail
            stream.Write(_character.Mails.unreadMailCount.TotalReceived);            // totalMail
            stream.Write(_character.Mails.unreadMailCount.TotalMiaReceived);         // totalMiaMail
            stream.Write(_character.Mails.unreadMailCount.TotalCommercialReceived);  // totalCommercialMail
            stream.Write(_character.Mails.unreadMailCount.UnreadReceived);           // unreadMail
            stream.Write(_character.Mails.unreadMailCount.UnreadMiaReceived);        // unreadMiaMail
            stream.Write(_character.Mails.unreadMailCount.UnreadCommercialReceived); // unreadCommercialMail
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
}
