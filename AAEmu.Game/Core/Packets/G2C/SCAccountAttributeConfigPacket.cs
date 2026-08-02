using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Declares the account-attribute domains implemented by the server.</summary>
/// <remarks>
/// <c>account_buff</c>, and <c>ulc</c>. Index 0 has no game-content kind and is reserved.
/// </remarks>
public class SCAccountAttributeConfigPacket() : GamePacket(SCOffsets.SCAccountAttributeConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(false); // Native account-attribute kind 0 is reserved.
        stream.Write(true);  // auction_post
        stream.Write(true);  // account_buff
        stream.Write(true);  // ulc
        return stream;
    }
}
