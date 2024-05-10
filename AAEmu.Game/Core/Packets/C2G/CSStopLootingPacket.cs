using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStopLootingPacket : GamePacket
{
    public CSStopLootingPacket() : base(CSOffsets.CSStopLootingPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var st = stream.ReadUInt32();

        Logger.Warn($"CSStopLootingPacket, st={st}");
    }
}
