using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTelescopeUnitsPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    private readonly bool _last;
    private readonly Slave[] _slaves;

    public SCTelescopeUnitsPacket(bool last, Slave[] slaves) : base(SCOffsets.SCTelescopeUnitsPacket, 1)
    {
        _last = last;
        _slaves = slaves;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_last);
        stream.Write((byte)_slaves.Length);
        foreach (var slave in _slaves)
        {
            stream.WriteBc(slave.ObjId);
            stream.Write(slave.Summoner?.Id ?? 0);
            stream.Write(2); // UnitType Slave
            stream.Write(slave.Template.Id);
            stream.WritePosition(slave.Transform.World.Position);
            stream.Write(slave.Faction?.Id ?? 0);
            stream.Write(slave.Expedition?.Id ?? 0);
            stream.Write(slave.Name);
            stream.Write(slave.Summoner?.Name ?? "");
            stream.Write(slave.Hp);
            stream.Write(slave.MaxHp);
            stream.Write(0u); // Don't know, but seems to be always zero
        }

        return stream;
    }
}
