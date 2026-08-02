using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Zw;

/// <summary>
/// ZWJoin wire body (sequential ISerialize from ZWJoinPacket_SerializeBody):
/// u32 p_from, u32 p_to, s32 id, u32 ip, u16 port, u64 accountId, u32 iid, u8 dev.
/// </summary>
public class ZWJoinPacket
{
    public uint PFrom { get; private set; }
    public uint PTo { get; private set; }
    public int Id { get; private set; }
    public uint Ip { get; private set; }
    public ushort Port { get; private set; }
    public ulong AccountId { get; private set; }
    public uint InstanceId { get; private set; }
    public bool Dev { get; private set; }

    public static ZWJoinPacket Read(PacketStream stream)
    {
        return new ZWJoinPacket
        {
            PFrom = stream.ReadUInt32(),
            PTo = stream.ReadUInt32(),
            Id = stream.ReadInt32(),
            Ip = stream.ReadUInt32(),
            Port = stream.ReadUInt16(),
            AccountId = stream.ReadUInt64(),
            InstanceId = stream.ReadUInt32(),
            Dev = stream.ReadByte() != 0
        };
    }
}
