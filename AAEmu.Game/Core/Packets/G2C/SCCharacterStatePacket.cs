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
            stream.Write(_character.InstanceId); // instanceId
            stream.Write(_character.Guid); // guid
            stream.Write(0); // rwd

            _character.Write(stream);

            stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0 }); //angles
            stream.Write(_character.Expirience);     // exp
            stream.Write(_character.RecoverableExp); // recoverableExp
            stream.Write(0u);                        // penaltiedExp
            stream.Write(0);                         // returnDistrictId
            stream.Write((uint)0);                   // returnDistrict -> type(id)
            stream.Write((uint)0);                   // resurrectionDistrict -> type(id)

            for (var i = 0; i < 11; i++)             // in 1.2 = 11, in 3.5 = 13
            {
                stream.Write((uint)0); // abilityExp
            }

            stream.Write(0);                                                   // totalSentMail add in 1200 march
            stream.Write(0);                                                   // totalMail add in 1200 march
            stream.Write(0);                                                   // totalMiaMail add in 1200 march
            stream.Write(0);                                                   // totalCommercialMail add in 1200 march
            stream.Write(_character.Mails.unreadMailCount.Received);           // unreadMail
            stream.Write(_character.Mails.unreadMailCount.MiaReceived);        // unreadMiaMail
            stream.Write(_character.Mails.unreadMailCount.CommercialReceived); // unreadCommercialMail
            stream.Write(_character.NumInventorySlots);                        // numInvenSlots
            stream.Write(_character.NumBankSlots);                             // numBankSlots
            stream.Write(_character.Money);                                    // moneyAmount - Inventory
            stream.Write(_character.Money2);                                   // moneyAmount - Bank
            stream.Write(0L);                                                  // moneyAmount
            stream.Write(0L);                                                  // moneyAmount

            stream.Write(_character.AutoUseAAPoint);

            stream.Write(0);               // juryPoint
            stream.Write(0);               // jailSeconds

            stream.Write(0L);              // bountyMoney
            stream.Write(0L);              // bountyTime

            stream.Write(0);               // reportedNo
            stream.Write(0);               // suspectedNo
            stream.Write(0);               // totalPlayTime

            stream.Write(DateTime.UtcNow); // createdTime

            stream.Write(_character.ExpandedExpert);

            return stream;
        }
    }
}
