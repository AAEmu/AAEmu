using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Gimmicks;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGimmicksCreatedPacket(Gimmick[] gimmicks) : GamePacket(SCOffsets.SCGimmicksCreatedPacket, 5)
{
    public const int MaxCountPerPacket = 30;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)gimmicks.Length); // TODO max length 30
        foreach (var gimmick in gimmicks)
        {
            gimmick.Write(stream);
        }

        return stream;
    }
}
