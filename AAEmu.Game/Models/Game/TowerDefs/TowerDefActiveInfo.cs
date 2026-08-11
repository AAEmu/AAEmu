using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.TowerDefs;

/// <summary>
/// One live tower event for <see cref="G2C.SCTowerDefActiveInfoListPacket"/> (world-map marks).
/// </summary>
public sealed class TowerDefActiveInfo : PacketMarshaler
{
    public uint ZoneId { get; set; }
    public uint CurrentStep { get; set; }
    public uint TowerDefId { get; set; }
    public ushort ZoneGroupId { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ZoneId);
        stream.Write(CurrentStep);
        stream.Write(TowerDefId);
        stream.Write(ZoneGroupId);
        return stream;
    }
}
