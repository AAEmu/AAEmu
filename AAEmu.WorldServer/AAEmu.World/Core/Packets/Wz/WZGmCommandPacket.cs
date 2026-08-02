using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZGmCommand (0x04F) — World → Zone native GM.
/// The command is a single byte on the zone link even though CSGmCommand carries it as u16.
/// </summary>
public class WZGmCommandPacket(uint unitId, byte cmd, string parameters) : ZonePacket(WzOpcodes.GmCommand)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(cmd);
        stream.Write(parameters ?? "");
    }
}
