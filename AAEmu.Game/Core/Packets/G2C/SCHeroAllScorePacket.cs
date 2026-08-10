using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One hero's progress through their term.</summary>
/// <param name="CharacterId">Which hero. Matched against the client's own hero list for name and grade.</param>
/// <param name="Score">Leadership earned in the current period.</param>
/// <param name="PeriodScore">The same figure; see the packet's remarks for why both are sent.</param>
/// <param name="MobilizationCount">Mobilization orders issued this term.</param>
/// <param name="MissionProgress">today_quest_steps id to times completed this term.</param>
public readonly record struct HeroScoreEntry(
    ulong CharacterId,
    int Score,
    int PeriodScore,
    int MobilizationCount,
    IReadOnlyList<KeyValuePair<uint, int>> MissionProgress);

/// <summary>
/// One nation's heroes and how their term is going - the Mission Status tab.
/// </summary>
/// <remarks>
/// Answers CSHeroAllScore and raises HERO_ALL_SCORE_UPDATED, which hero_mission.lua turns into rows of
/// "World Bosses (n/100)" and "Earning Leadership 3000 or above (n/3000)".
///
/// Only three figures per hero are actually on the wire. The binding behind X2Hero:GetFactionScores
/// (.text 0x19db10) takes the row's name, expedition, grade and ranking from the client's OWN hero
/// record - the one SCHeroList already delivered, read at +0x18 and +0x24 - and takes every target from
/// client-side data: maxScore and maxMobilizationOrderCount come out of a hero_bonuses row, and each
/// mission's targetCount out of hero_bonus_today_assignments. So this packet reports progress and
/// nothing else, and a hero missing from SCHeroList would not render however complete this is.
///
/// score and periodScore are the same figure read at two different times. The binding picks between
/// them on a comparison of the hero_period and leadership_ranking schedule windows (0x19db9b, stored at
/// rbp+0x118) and uses periodScore when that holds, score otherwise. Which way round is not worth
/// pinning down: both carry the leadership earned in the current period, so the tab reads correctly
/// whichever branch the client takes.
///
/// The second field is read into the row and never looked at again by that binding. It is sent as the
/// nation for want of anything better; nothing observable depends on it.
/// </remarks>
public class SCHeroAllScorePacket(uint nationId, IReadOnlyList<HeroScoreEntry> scores)
    : GamePacket(SCOffsets.SCHeroAllScorePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((int)nationId);
        stream.Write(scores.Count);

        foreach (var entry in scores)
        {
            stream.Write(entry.CharacterId);
            stream.Write((int)nationId);
            stream.Write(entry.Score);
            stream.Write(entry.PeriodScore);
            stream.Write(entry.MobilizationCount);

            // map<todayQuestStepId, timesCompleted>, size first. Empty is not a gap in the display: the
            // binding walks the client's own list of hero missions and looks each one up here, so an
            // absent step renders as 0 against its target rather than vanishing from the row.
            stream.Write(entry.MissionProgress.Count);
            foreach (var (stepId, progress) in entry.MissionProgress)
            {
                stream.Write((int)stepId);
                stream.Write(progress);
            }
        }

        return stream;
    }
}
