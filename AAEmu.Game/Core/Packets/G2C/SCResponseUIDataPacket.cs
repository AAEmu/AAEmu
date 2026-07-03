using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResponseUIDataPacket(uint characterId, ushort uiDataType, string uiData)
    : GamePacket(SCOffsets.SCResponseUIDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 body (x2game-dev_dedicate SCResponseUIData serializer sub_39C28210):
        // type(i64 charId) | uiDataType(u16) | uiData(length-prefixed string) | size(u32).
        stream.Write((ulong)characterId);
        stream.Write(uiDataType);
        stream.Write(uiData);
        stream.Write(uiData.Length + 1);
        return stream;
    }
}
