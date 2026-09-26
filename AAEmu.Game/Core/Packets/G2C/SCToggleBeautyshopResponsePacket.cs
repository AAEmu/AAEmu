using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One bool "isToggle". The client handler
/// enters shop mode on true (sets the flag) and leaves it on false (clears
/// it), so false is both the refusal and the answer to CSLeaveBeautyshop.
/// </summary>
public class SCToggleBeautyshopResponsePacket(bool isToggle) : GamePacket(SCOffsets.SCToggleBeautyshopResponsePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isToggle);
        return stream;
    }
}
