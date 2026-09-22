using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One bool "isToggle" (x2game-dev.dll serializer FUN_39c53c00). The client handler (FUN_394dc880)
/// enters shop mode on true (FUN_3970c8d0 sets the flag) and leaves it on false (FUN_3970ae50 clears
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
