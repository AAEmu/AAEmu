using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// A resident contributes service points to a zone group: settled into
/// <c>character_resident_state</c>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: i16 zone group, u64 type2, u32 point. type2's meaning is not
/// pinned by the client surface and is forwarded unused — never guessed at.
/// </remarks>
public class CSAddResidentServicePointPacket() : GamePacket(CSOffsets.CSAddResidentServicePointPacket, 1)
{
    public short TypeValue { get; private set; }
    public ulong TypeValue2 { get; private set; }
    public uint Point { get; private set; }

    public override void Read(PacketStream stream)
    {

        TypeValue = stream.ReadInt16();
        TypeValue2 = stream.ReadUInt64();
        Point = stream.ReadUInt32();
        while (stream.HasBytes)
            stream.ReadByte(); // Fix: drain tail

        HousingManager.Instance.ResidentAddServicePoint(Connection, TypeValue, TypeValue2, Point);
    }
}
