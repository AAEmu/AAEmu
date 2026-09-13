using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSExpeditionRecruitmentAddPacket() : GamePacket(CSOffsets.CSExpeditionRecruitmentAddPacket, 1)
{
    public short Interest { get; private set; }
    public uint Day { get; private set; }
    public string Introduce { get; private set; }

    public override void Read(PacketStream stream)
    {
        Interest = stream.ReadInt16();
        Day = stream.ReadUInt32();
        Introduce = stream.ReadString();
        var result = ExpeditionRecruitmentPacketService.Get().Register(Connection.ActiveChar, Interest, Day,
            Introduce, DateTime.UtcNow);
        if (result == ExpeditionRecruitmentResult.Success)
            Connection.ActiveChar.SendPacket(new SCExpeditionRecruitmentAddPacket());
    }
}
