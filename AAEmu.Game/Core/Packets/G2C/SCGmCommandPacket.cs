using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 10.0.2.13 SCGmCommand (0x1EF): bc unitId, u8 cmd, u8 level, string params, string feedback.
/// </summary>
public class SCGmCommandPacket(uint unitId, byte cmd, byte level, string parameters, string feedback)
    : GamePacket(SCOffsets.SCGmCommandPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(cmd);
        stream.Write(level);
        stream.Write(parameters ?? "");
        stream.Write(feedback ?? "");
        return stream;
    }
}
