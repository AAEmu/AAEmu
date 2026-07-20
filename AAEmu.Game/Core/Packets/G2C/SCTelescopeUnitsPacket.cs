using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTelescopeUnitsPacket(bool last, Slave[] slaves) : GamePacket(SCOffsets.SCTelescopeUnitsPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(last);
        stream.Write((byte)slaves.Length);
        foreach (var slave in slaves)
        {
            stream.WriteBc(slave.ObjId);
            stream.Write(slave.Summoner?.Id ?? 0);
            stream.Write(2); // UnitType Slave
            stream.Write(slave.Template.Id);
            stream.WritePosition(slave.Transform.World.Position);
            stream.Write((uint)(slave.Faction?.Id ?? 0));
            stream.Write((uint)(slave.Expedition?.Id ?? 0));
            stream.Write(slave.Name);
            stream.Write(slave.Summoner?.Name ?? "");
            stream.Write(slave.Hp);
            stream.Write(slave.MaxHp);
            stream.Write(0u); // Don't know, but seems to be always zero
        }

        return stream;
    }
}
