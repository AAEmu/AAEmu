using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeExpeditionSponsorPacket() : GamePacket(CSOffsets.CSChangeExpeditionSponsorPacket, 1)
{
    public int ExpectedSponsorId { get; private set; }
    public int SponsorId { get; private set; }

    public override void Read(PacketStream stream)
    {
        ExpectedSponsorId = stream.ReadInt32();
        SponsorId = stream.ReadInt32();

        var character = Connection.ActiveChar;
        if (character?.Expedition == null || ExpectedSponsorId <= 0 || SponsorId <= 0 ||
            ExpeditionActivityServices.Get().ChangeSponsor(character, (uint)ExpectedSponsorId, (uint)SponsorId))
            return;

        character.SendPacket(new SCExpeditionSponsorChangedPacket(character.Expedition, false));
    }
}
