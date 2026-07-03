using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActabilityPacket(bool last, Actability[] actabilities) : GamePacket(SCOffsets.SCActabilityPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 body (x2game-dev_dedicate SCActability serializer sub_39C3A820 / per-entry sub_39A768E0):
        // last(bool) | count(u8, max 100) | per entry: pish/pisc(id, point) then step(u8). The old fixed
        // id(u32)+point(u32)+step layout is ~4 bytes too long per entry and trips the client size check.
        stream.Write(last);
        stream.Write((byte)actabilities.Length);
        foreach (var actability in actabilities)
        {
            stream.WritePisc(actability.Id, (uint)actability.Point);
            stream.Write(actability.Step);
        }

        return stream;
    }
}
