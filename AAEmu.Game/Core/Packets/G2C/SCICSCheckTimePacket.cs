using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Forces the client to re-request the shop list, use it whenever the shop list needs refreshing
/// </summary>
public class SCICSCheckTimePacket : GamePacket
{
    public SCICSCheckTimePacket() : base(SCOffsets.SCICSCheckTimePacket, 5)
    {
    }
}
