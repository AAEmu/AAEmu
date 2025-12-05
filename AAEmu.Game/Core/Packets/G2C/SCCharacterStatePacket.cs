using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterStatePacket(Character character) : GamePacket(SCOffsets.SCCharacterStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.Transform.InstanceId); // instanceId
        stream.Write(character.Guid); // guid
        stream.Write(0); // rwd

        character.Write(stream);

        stream.Write([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0]); //angles
        stream.Write(character.Experience);
        stream.Write(character.RecoverableExp);
        stream.Write(0u); // penaltiedExp
        stream.Write(0); // returnDistrictId
        stream.Write((uint)0); // returnDistrict -> type(id)
        stream.Write((uint)0); // resurrectionDistrict -> type(id)

        for (var i = 0; i < 11; i++)
            stream.Write((uint)0); // abilityExp

        stream.Write(character.Mails.UnreadMailCount.Received); // unreadMail
        stream.Write(character.Mails.UnreadMailCount.MiaReceived); // unreadMiaMail
        stream.Write(character.Mails.UnreadMailCount.CommercialReceived); // unreadCommercialMail
        stream.Write(character.NumInventorySlots);
        stream.Write(character.NumBankSlots);
        stream.Write(character.Money); // moneyAmount - Inventory
        stream.Write(character.Money2); // moneyAmount - Bank
        stream.Write(0L); // moneyAmount
        stream.Write(0L); // moneyAmount

        stream.Write(character.AutoUseAAPoint);

        stream.Write(0); // juryPoint
        stream.Write(0); // jailSeconds

        stream.Write(0L); // bountyMoney
        stream.Write(0L); // bountyTime

        stream.Write(0); // reportedNo
        stream.Write(0); // suspectedNo
        stream.Write(0); // totalPlayTime

        stream.Write(DateTime.UtcNow); // createdTime

        stream.Write(character.ExpandedExpert);

        return stream;
    }
}
