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
            stream.Write(_character.InstanceId); // iid
            stream.Write(_character.Guid);       // guid
            stream.Write(0);                     // rwd

            _character.Write(stream); // CharacterReader

            stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0 }); //angles
            stream.Write(_character.Expirience);     // exp
            stream.Write(_character.RecoverableExp); // recoverableExp
            stream.Write(0u);                        // penaltiedExp
            stream.Write(0);                         // returnDistrictId
            stream.Write((uint)0);                   // returnDistrict -> type(id)
            stream.Write((uint)0);                   // resurrectionDistrict -> type(id)

            for (var i = 0; i < 11; i++)             // in 1.2 = 11, in 1.7 = 11, in 3.5 = 13
            {
                stream.Write((uint)0); // abilityExp
            }

            stream.Write(_character.Mails.unreadMailCount.TotalSent);                // totalSentMail add in 1200 march
            stream.Write(_character.Mails.unreadMailCount.TotalReceived);            // totalMail add in 1200 march
            stream.Write(_character.Mails.unreadMailCount.TotalMiaReceived);         // totalMiaMail add in 1200 march
            stream.Write(_character.Mails.unreadMailCount.TotalCommercialReceived);  // totalCommercialMail add in 1200 march
            stream.Write(_character.Mails.unreadMailCount.UnreadReceived);           // unreadMail
            stream.Write(_character.Mails.unreadMailCount.UnreadMiaReceived);        // unreadMiaMail
            stream.Write(_character.Mails.unreadMailCount.UnreadCommercialReceived); // unreadCommercialMail
            stream.Write(_character.NumInventorySlots);                              // numInvenSlots
            stream.Write(_character.NumBankSlots);                                   // numBankSlots
            stream.Write(_character.Money);                                          // moneyAmount - Inventory
            stream.Write(_character.Money2);                                         // moneyAmount - Bank
            stream.Write(0L);                                                        // moneyAmount
            stream.Write(0L);                                                        // moneyAmount

            stream.Write(_character.AutoUseAAPoint);

            stream.Write(0);               // juryPoint
            stream.Write(0);               // jailSeconds
            stream.Write(0L);              // bountyMoney
            stream.Write(0L);              // bountyTime
            stream.Write(0);               // reportedNo
            stream.Write(0);               // suspectedNo

            _character.TotalPlayTime = _character.CreatedTime.Hour + DateTime.UtcNow.Hour;
            stream.Write(_character.TotalPlayTime); // totalPlayTime
            stream.Write(_character.CreatedTime); // createdTime

            stream.Write(_character.ExpandedExpert);

            // added in 1.7
            stream.Write(0L);               // nationJoinTime
            stream.Write((byte)0);          // remainBotCheckCnt

            return stream;
        }
    }
}
