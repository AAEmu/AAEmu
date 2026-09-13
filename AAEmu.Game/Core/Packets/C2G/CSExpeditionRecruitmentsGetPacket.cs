using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSExpeditionRecruitmentsGetPacket() : GamePacket(CSOffsets.CSExpeditionRecruitmentsGetPacket, 1)
{
    public bool My { get; private set; }
    public ushort Page { get; private set; }
    public short Interest { get; private set; }
    public int TypeValue { get; private set; }
    public int TypeValue2 { get; private set; }
    public string Name { get; private set; }
    public sbyte SortType { get; private set; }

    public override void Read(PacketStream stream)
    {
        My = stream.ReadBoolean();
        Page = stream.ReadUInt16();
        Interest = stream.ReadInt16();
        TypeValue = stream.ReadInt32();
        TypeValue2 = stream.ReadInt32();
        Name = stream.ReadString();
        SortType = stream.ReadSByte();
        var service = ExpeditionRecruitmentPacketService.Get();
        var page = service.Search(Connection.ActiveChar, new(Connection.ActiveChar.Id, Page, TypeValue, TypeValue2,
            Name, SortType, Interest, My), DateTime.UtcNow);
        var applied = service.GetMyApplications(Connection.ActiveChar).Select(x => x.ExpeditionId).ToHashSet();
        Connection.ActiveChar.SendPacket(new SCExpeditionRecruitmentsGetPacket(page, applied));
    }
}
