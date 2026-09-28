using System.IO;

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

        // Register upserts: it overwrites the guild's recruitment introduction and restarts its
        // expiry. A short read degrades Day to 0 and Introduce to "", and both pass their own
        // validation, so Overran has to be tested here rather than left to the stream. The
        // dispatch arms strict reads for this body, but a handler that can be reached another way
        // should not depend on that.
        if (stream.Overran)
            throw new InvalidDataException("ExpeditionRecruitmentAdd: truncated body");

        var result = ExpeditionRecruitmentPacketService.Get().Register(Connection.ActiveChar, Interest, Day,
            Introduce, DateTime.UtcNow);
        if (result == ExpeditionRecruitmentResult.Success)
            Connection.ActiveChar.SendPacket(new SCExpeditionRecruitmentAddPacket());
    }
}
