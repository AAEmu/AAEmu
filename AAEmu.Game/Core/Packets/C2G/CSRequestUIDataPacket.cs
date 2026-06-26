using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestUIDataPacket() : GamePacket(CSOffsets.CSRequestUIDataPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // The 1.2 SCResponseUIData (0x246) body layout corrupts the 10.0.2.13 client's recv
        // ("not enough buffer for size" + SC count desync). UI data (saved layouts/keybinds) is not
        // needed at char-select, so don't respond until SCResponseUIData is rebuilt for 10.0.2.13.
        _ = stream.ReadUInt16(); // uiDataType
        _ = stream.ReadUInt32(); // id
        Logger.Debug("RequestUIData (no reply — SCResponseUIData not yet 10.0.2.13)");
    }
}
