using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCActabilityPacket(bool last, Actability[] actabilities) : GamePacket(SCOffsets.SCActabilityPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)actabilities.Length); // TODO max count 100
        foreach (var actability in actabilities)
        {
            stream.Write(actability.Id);
            stream.Write(actability.Point);
            stream.Write(actability.Step);
        }

        return stream;
    }
}
