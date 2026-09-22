using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;

using static AAEmu.Game.Models.Game.Quests.StarterChainActorRules;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// The six race starter chains as their quest_acts rows stand in the 10.0.2.13 content (quest and
/// act ids in the comments), reduced to the actors each chain needs in the world. Sphere starts,
/// cinemas, timers and supplies ride along so the reduction is shown to ignore them.
/// </summary>
public class StarterChainActorRulesTests
{
    // Nuian: 6839 (sphere 2321) -> 330 -> 2531, w_solzreed_3 (zones.id 125, zone_key 179).
    public static readonly ActorAct[] Nuian =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 6839 act 42988, sphere 2321
        new("QuestActObjCinema"),                                   // 6839 act 43238, cinema 163
        new("QuestActConAutoComplete"),                             // 6839 act 42991
        new(nameof(QuestActConAcceptNpc), NpcId: 3597),             // 330 act 9874
        new(nameof(QuestActConReportNpc), NpcId: 11541),            // 330 act 2438
        new(nameof(QuestActConAcceptNpc), NpcId: 11541),            // 2531 act 15458
        new(nameof(QuestActConReportNpc), NpcId: 10580),            // 2531 act 15459
    ];

    // Dwarf: 5811 (sphere 2613) -> 3484 -> 3485, w_cradle_of_genesis_2 (zones.id 238, zone_key 328).
    public static readonly ActorAct[] Dwarf =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 5811 act 53526, sphere 2613
        new(nameof(QuestActConAcceptNpc), NpcId: 5800),             // 3484 act 35033
        new(nameof(QuestActConReportNpc), NpcId: 13874),            // 3484 act 35034
        new(nameof(QuestActConAcceptNpc), NpcId: 13874),            // 3485 act 35035
        new(nameof(QuestActObjItemGather), HighlightDoodadId: 11641), // 3485 act 53527, item 41439
        new(nameof(QuestActConReportDoodad), DoodadId: 14206),      // 3485 act 64246
    ];

    // Elf: 6840 (sphere 2322) -> 2385 -> 2386 -> 2387, w_gweonid_forest_1 (zones.id 1, zone_key 129).
    public static readonly ActorAct[] Elf =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 6840 act 42992, sphere 2322
        new(nameof(QuestActConAcceptNpc), NpcId: 390),              // 2385 act 14478
        new(nameof(QuestActConReportNpc), NpcId: 7816),             // 2385 act 14479
        new(nameof(QuestActConAcceptNpc), NpcId: 7816),             // 2386 act 14480
        new(nameof(QuestActObjInteraction), DoodadId: 2881, HighlightDoodadId: 2881), // 2386 act 27476
        new(nameof(QuestActConReportNpc), NpcId: 7816),             // 2386 act 21442
        new(nameof(QuestActConAcceptNpc), NpcId: 7816),             // 2387 act 14482
        new(nameof(QuestActConReportDoodad), DoodadId: 14178),      // 2387 act 64210
    ];

    // Hariharan: 6842 (sphere 2324) -> 1112 -> 1113, e_rainbow_field_2 (zones.id 133, zone_key 187).
    public static readonly ActorAct[] Hariharan =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 6842 act 43004, sphere 2324
        new(nameof(QuestActConAcceptNpc), NpcId: 2477),             // 1112 act 12215
        new(nameof(QuestActConReportNpc), NpcId: 2473),             // 1112 act 12216
        new(nameof(QuestActConAcceptNpc), NpcId: 2473),             // 1113 act 12217
        new(nameof(QuestActObjItemGather), HighlightDoodadId: 1427), // 1113 act 12952, item 13974
        new(nameof(QuestActConReportNpc), NpcId: 2472),             // 1113 act 12219
    ];

    // Ferre: 6841 (sphere 2323) -> 1212 -> 1213, e_falcony_plateau_2 (zones.id 130, zone_key 184).
    public static readonly ActorAct[] Ferre =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 6841 act 43000, sphere 2323
        new(nameof(QuestActConAcceptNpc), NpcId: 4220),             // 1212 act 8031
        new(nameof(QuestActConReportNpc), NpcId: 6045),             // 1212 act 8032
        new(nameof(QuestActConAcceptNpc), NpcId: 6045),             // 1213 act 8034
        new(nameof(QuestActObjInteraction), DoodadId: 1473, HighlightDoodadId: 1473), // 1213 act 27765
        new(nameof(QuestActConReportNpc), NpcId: 4236),             // 1213 act 8037
    ];

    // Warborn: 8228 (sphere 2600) -> 8159 -> 8160, e_sunny_wilderness_4 (zones.id 227) next to the
    // birth partition e_sunny_wilderness_1 (zone_key 157), where all three NPCs are placed.
    public static readonly ActorAct[] Warborn =
    [
        new(nameof(QuestActConAcceptSphere)),                       // 8228 act 53133, sphere 2600
        new(nameof(QuestActConAcceptNpc), NpcId: 17433),            // 8159 act 53518
        new(nameof(QuestActObjInteraction), DoodadId: 11624, HighlightDoodadId: 11624), // 8159 act 53517
        new("QuestActConAutoComplete"),                             // 8159 act 53519
        new(nameof(QuestActConAcceptNpc), NpcId: 16989),            // 8160 act 54587
        new("QuestActCheckTimer"),                                  // 8160 act 54588
        new(nameof(QuestActConReportNpc), NpcId: 17410),            // 8160 act 53938
    ];

    public static IEnumerable<ActorAct> AllChains =>
        Nuian.Concat(Dwarf).Concat(Elf).Concat(Hariharan).Concat(Ferre).Concat(Warborn);

    private static string Join(IEnumerable<uint> ids) => string.Join(",", ids);

    [Test]
    public async Task Nuian_ChainNeedsThreeNpcsAndNoDoodad()
    {
        var actors = Collect(Nuian);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("3597,10580,11541");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("");
    }

    [Test]
    public async Task Dwarf_ChainNeedsTwoNpcsHerbAndDaughterBody()
    {
        var actors = Collect(Dwarf);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("5800,13874");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("11641,14206");
    }

    [Test]
    public async Task Elf_ChainNeedsTwoNpcsDaggerTableAndArenaBody()
    {
        var actors = Collect(Elf);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("390,7816");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("2881,14178");
    }

    [Test]
    public async Task Hariharan_ChainNeedsThreeNpcsAndDragonfruit()
    {
        var actors = Collect(Hariharan);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("2472,2473,2477");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("1427");
    }

    [Test]
    public async Task Ferre_ChainNeedsThreeNpcsAndWindstone()
    {
        var actors = Collect(Ferre);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("4220,4236,6045");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("1473");
    }

    [Test]
    public async Task Warborn_ChainNeedsThreeNpcsAndFlowerBed()
    {
        var actors = Collect(Warborn);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("16989,17410,17433");
        await Assert.That(Join(actors.Doodads)).IsEqualTo("11624");
    }

    [Test]
    public async Task Collect_SphereStartCinemaAndAutoCompleteNameNoActor()
    {
        // 6839 alone: the sphere start, its cinema and the auto complete.
        var actors = Collect(Nuian.Take(3));

        await Assert.That(actors.Npcs.Count).IsEqualTo(0);
        await Assert.That(actors.Doodads.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Collect_ZeroIdsAndNullAreEmpty()
    {
        var zeroes = Collect(
        [
            new(nameof(QuestActConAcceptNpc), NpcId: 0),
            new(nameof(QuestActConReportDoodad), DoodadId: 0),
            new(nameof(QuestActObjInteraction), DoodadId: 0, HighlightDoodadId: 0),
        ]);

        await Assert.That(zeroes.Npcs.Count).IsEqualTo(0);
        await Assert.That(zeroes.Doodads.Count).IsEqualTo(0);
        await Assert.That(Collect(null).Npcs.Count).IsEqualTo(0);
        await Assert.That(Collect(null).Doodads.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Collect_HuntTargetIsAnNpcActor()
    {
        // 1222 act 63967: hunt one of npc 7730.
        var actors = Collect([new(nameof(QuestActObjMonsterHunt), NpcId: 7730)]);

        await Assert.That(Join(actors.Npcs)).IsEqualTo("7730");
    }

    [Test]
    public async Task Collect_ItemUseCountsOnlyItsHighlightDoodad()
    {
        // 8161 act 53945 uses item 23635 with no highlight; act 53946 gathers 21850 off doodad 4594.
        var actors = Collect(
        [
            new(nameof(QuestActObjItemUse), HighlightDoodadId: 0),
            new(nameof(QuestActObjItemGather), HighlightDoodadId: 4594),
        ]);

        await Assert.That(Join(actors.Doodads)).IsEqualTo("4594");
    }

    [Test]
    public async Task RaceBit_MatchesQuestContextsRaceMask()
    {
        // quest_contexts.race counts per exact mask: 1 (72 rows), 4 (120), 8 (71), 16 (80), 32 (81), 128 (98).
        await Assert.That(RaceBit(Race.Nuian)).IsEqualTo((byte)1);
        await Assert.That(RaceBit(Race.Dwarf)).IsEqualTo((byte)4);
        await Assert.That(RaceBit(Race.Elf)).IsEqualTo((byte)8);
        await Assert.That(RaceBit(Race.Hariharan)).IsEqualTo((byte)16);
        await Assert.That(RaceBit(Race.Ferre)).IsEqualTo((byte)32);
        await Assert.That(RaceBit(Race.Warborn)).IsEqualTo((byte)128);
        await Assert.That(RaceBit(Race.None)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task IsStarterChainQuest_AcceptsTheStartZoneChain()
    {
        // 330: race 1, level 1, zone 125 in group 5; the nuian start key 179 is in group 5.
        await Assert.That(IsStarterChainQuest(1, 1, 5, Race.Nuian, 5)).IsTrue();
        // 8160: race 128, level 5, zone 227 in group 13; the warborn start key 157 is in group 13.
        await Assert.That(IsStarterChainQuest(128, 5, 13, Race.Warborn, 13)).IsTrue();
    }

    [Test]
    public async Task IsStarterChainQuest_RejectsPrologueAnyRaceAndHigherLevels()
    {
        // 8225: warborn prologue in o_room_of_queen_1 (zone 252, group 98), not the start group.
        await Assert.That(IsStarterChainQuest(128, 1, 98, Race.Warborn, 13)).IsFalse();
        // 6280: race 255 (any race) in the nuian start group.
        await Assert.That(IsStarterChainQuest(255, 35, 5, Race.Nuian, 5)).IsFalse();
        // 8165: warborn level 8 in the start group, past the cap.
        await Assert.That(IsStarterChainQuest(128, 8, 13, Race.Warborn, 13)).IsFalse();
        // 330 seen from the wrong race.
        await Assert.That(IsStarterChainQuest(1, 1, 5, Race.Elf, 5)).IsFalse();
        // No zone group on either side, or no race, is never a match.
        await Assert.That(IsStarterChainQuest(1, 1, 0, Race.Nuian, 5)).IsFalse();
        await Assert.That(IsStarterChainQuest(1, 1, 5, Race.Nuian, 0)).IsFalse();
        await Assert.That(IsStarterChainQuest(0, 1, 5, Race.None, 5)).IsFalse();
    }
}
