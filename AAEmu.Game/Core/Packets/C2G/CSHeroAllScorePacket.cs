using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The Mission Status tab asking for one nation's hero progress.
/// </summary>
/// <remarks>
/// Sent by X2Hero:RequestFactionScores whenever the tab's faction combobox settles on a nation, which
/// includes opening the Hero window - hero.lua sends it alongside the ranking request, which is why an
/// unregistered opcode used to show up as "Unknown packet 0x1A8" at login.
///
/// The body is the nation to report on, and it is answered as asked rather than being forced to the
/// player's own: the combobox is filled from X2Hero:GetHeroFactions, so a client can legitimately ask
/// about any nation it was offered, and the reply carries nothing private - the same roster and the same
/// leadership figures the Current Heroes tab already shows everyone.
/// </remarks>
public class CSHeroAllScorePacket() : GamePacket(CSOffsets.CSHeroAllScorePacket, 1)
{
    public int NationId { get; private set; }

    public override void Read(PacketStream stream)
    {
        NationId = stream.ReadInt32();

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        // 0 is the combobox's "no selection" state; hero_mission.lua clears the list itself in that case
        // and never gets as far as reading a reply.
        if (NationId <= 0)
            return;

        var nation = (uint)NationId;
        character.SendPacket(new SCHeroAllScorePacket(nation, HeroManager.Instance.BuildScores(nation)));
    }
}
