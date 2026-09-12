using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x34B. The client serializer at <c>FUN_39C885B0</c> exposes <paramref name="updatedFlags"/>
/// as a raw signed 16-bit field; its bit meanings are not yet established.
/// </summary>
public class SCButlerInfoUpdatedPacket(
    ushort errorMessage,
    short updatedFlags,
    bool notifyMessage,
    bool resetAllActability,
    uint laborPower,
    ushort lpChargedAmount,
    ushort remainProductionCost,
    string name)
    : GamePacket(SCOffsets.SCButlerInfoUpdatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(errorMessage);
        stream.Write(updatedFlags);
        stream.Write(notifyMessage);
        stream.Write(resetAllActability);
        stream.Write(laborPower);
        stream.Write(lpChargedAmount);
        stream.Write(remainProductionCost);
        stream.Write(name);
        return stream;
    }
}
