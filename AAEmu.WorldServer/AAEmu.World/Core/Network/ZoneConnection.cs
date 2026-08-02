using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.World.Core.Packets;
using AAEmu.World.Core.Zone;

namespace AAEmu.World.Core.Network;

public class ZoneConnection
{
    private readonly ISession _session;

    public uint Id => _session.SessionId;
    public string Ip => _session.Ip.ToString();
    public PacketStream? LastPacket { get; set; }
    public ZoneConnectionState State { get; set; } = ZoneConnectionState.Connected;
    /// <summary>Zone key from ZWJoin.id (e.g. 129 = w_gweonid_forest_1).</summary>
    public uint ZoneId { get; set; }
    /// <summary>ZWJoin.iid — 0 = main-world default; non-zero reserved for instances.</summary>
    public uint InstanceId { get; set; }
    public UnitRegistry Units { get; } = new();

    public ZoneConnection(ISession session)
    {
        _session = session;
    }

    public void SendPacket(ZonePacket packet) => _session.SendPacket(packet.Encode());

    public void SendRaw(byte[] data) => _session.SendPacket(data);

    public void Close() => _session.Close();
}
