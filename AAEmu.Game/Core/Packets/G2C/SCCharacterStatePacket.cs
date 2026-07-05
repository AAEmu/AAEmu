using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterStatePacket(Character character) : GamePacket(SCOffsets.SCCharacterStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(character.Transform.InstanceId); // instanceId
        stream.Write(character.Guid); // guid
        stream.Write(0); // rwd

        character.Write(stream);

        stream.Write(character.Transform.World.Rotation.X);
        stream.Write(character.Transform.World.Rotation.Y);
        stream.Write(character.Transform.World.Rotation.Z);
        stream.Write(character.Experience);
        stream.Write(character.RecoverableExp);
        stream.Write(0u); // penaltiedExp
        stream.Write(character.ReturnDistrictId); // returnDistrictId
        stream.Write((uint)0); // returnDistrict -> type(id)
        stream.Write(character.ResurrectionDistrictId); // resurrectionDistrict -> type(id)

        for (var i = AbilityType.General; i < AbilityType.None; i++)
            stream.Write((uint)character.Abilities.Abilities[i].Exp); // abilityExp

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

        stream.Write(character.JuryPoint); // juryPoint
        stream.Write(0); // jailSeconds

        stream.Write(0L); // bountyMoney
        stream.Write(0L); // bountyTime

        stream.Write(character.ReportedAsBotCount); // reportedNo
        stream.Write(0); // suspectedNo
        stream.Write((int)character.OnlineTime.TotalSeconds); // totalPlayTime

        stream.Write(character.Created); // createdTime

        stream.Write(character.ExpandedExpert);

        return stream;
    }
}
