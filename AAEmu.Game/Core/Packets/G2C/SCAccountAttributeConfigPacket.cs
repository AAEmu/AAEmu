using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Declares the account-attribute domains this world actually uses.</summary>
public class SCAccountAttributeConfigPacket() : GamePacket(SCOffsets.SCAccountAttributeConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        foreach (var used in AccountAttributeConfigRules.UsedFlags)
            stream.Write(used);
        return stream;
    }
}
