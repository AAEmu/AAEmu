using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x345. The 10.0.2.13 client serializer at <c>FUN_39C54B90</c> writes the house name
/// before the common butler state.
/// </summary>
public class SCButlerInitInfoPacket(string houseName, ButlerInfoWire info)
    : GamePacket(SCOffsets.SCButlerInitInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(houseName);
        WriteCommonInfo(stream, info);
        return stream;
    }

    internal static void WriteCommonInfo(PacketStream stream, ButlerInfoWire info)
    {
        stream.Write(info.WorldId);
        stream.Write(info.Name);
        stream.Write(info.HouseTlId);
        stream.Write(info.LaborPower);
        stream.Write(info.LpChargedAmount);
        stream.Write(info.RemainProductionCost);
    }
}
