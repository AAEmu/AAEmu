using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Faction;

public class FactionExileRulesTests
{
    private const uint Nuia = 148, Haranya = 149;

    private static readonly List<FactionChangeLimit> Limits =
    [
        new(-1, 10, 40), new(10, 20, 40), new(20, 30, 42), new(90, 100, 50)
    ];

    private static ExileRoute Route(ExileRouteMode mode = ExileRouteMode.Automatic, uint used = 0) =>
        new() { FromFactionId = Nuia, ToFactionId = Haranya, NpcSpawnerId = 21908, Mode = mode, Used = used };

    [Test]
    public async Task MaxFor_UsesTheLimitBandOfThePowerGap()
    {
        await Assert.That(FactionExileRules.MaxFor(Route(), Limits, 1000, 950)).IsEqualTo(40);
        await Assert.That(FactionExileRules.MaxFor(Route(), Limits, 1000, 750)).IsEqualTo(42);
        await Assert.That(FactionExileRules.MaxFor(Route(), Limits, 1000, 0)).IsEqualTo(50);
        await Assert.That(FactionExileRules.MaxFor(new ExileRoute { MaxOverride = 3 }, Limits, 1000, 0)).IsEqualTo(3);
    }

    [Test]
    public async Task Automatic_OpensOnlyTowardsAnInferiorFaction()
    {
        var none = new HashSet<uint>();

        await Assert.That(FactionExileRules.IsActive(Route(), 40, true, none, f => f == Haranya)).IsTrue();
        await Assert.That(FactionExileRules.IsActive(Route(), 40, true, none, _ => false)).IsFalse();
        await Assert.That(FactionExileRules.IsActive(Route(), 40, true, none, _ => true)).IsFalse();
    }

    [Test]
    public async Task Switches_ShutTheRoute()
    {
        var none = new HashSet<uint>();

        await Assert.That(FactionExileRules.IsActive(Route(ExileRouteMode.Open), 40, true, none, _ => false)).IsTrue();
        await Assert.That(FactionExileRules.IsActive(Route(ExileRouteMode.Open), 40, false, none, _ => false)).IsFalse();
        await Assert.That(FactionExileRules.IsActive(Route(ExileRouteMode.Closed), 40, true, none, f => f == Haranya)).IsFalse();
        await Assert.That(FactionExileRules.IsActive(Route(ExileRouteMode.Open), 40, true, new HashSet<uint> { Nuia }, _ => false)).IsFalse();
        await Assert.That(FactionExileRules.IsActive(Route(ExileRouteMode.Open, used: 40), 40, true, none, _ => false)).IsFalse();
    }

    [Test]
    public async Task Cooldown_RunsFromTheLastExile()
    {
        var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.That(FactionExileRules.CooldownOver(null, 5184000, at)).IsTrue();
        await Assert.That(FactionExileRules.CooldownOver(at, 5184000, at.AddDays(59))).IsFalse();
        await Assert.That(FactionExileRules.CooldownOver(at, 5184000, at.AddDays(60))).IsTrue();
    }

    [Test]
    public async Task LimitListPacket_WritesWeekCountAndEntries()
    {
        var body = new SCFactionImmigrateLimitListPacket(9, [new FactionImmigrateLimit(Haranya, Nuia, 40, 3, 0)])
            .Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(9u); expected.Write(1u); expected.Write(1u);
        expected.Write((int)Haranya); expected.Write((int)Nuia); expected.Write(40u); expected.Write(3u); expected.Write(0u);

        await Assert.That(Convert.ToHexString(body)).IsEqualTo(Convert.ToHexString(expected.GetBytes()));
        await Assert.That(SCOffsets.SCFactionImmigrateLimitListPacket).IsEqualTo((ushort)0x50);
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task IsLimitedExileQuest_OnlyForFactionChangesThatKeepTheLimits(bool ignoreLimit, bool expected)
    {
        var template = new QuestTemplate();
        var component = new QuestComponentTemplate(template);
        component.ActTemplates.Add(new QuestActSupplyFactionChange(component) { SystemFactionId = 161, IgnoreLimit = ignoreLimit });
        template.Components[1] = component;

        await Assert.That(FactionExileManager.IsLimitedExileQuest(template)).IsEqualTo(expected);
        await Assert.That(FactionExileManager.IsLimitedExileQuest(new QuestTemplate())).IsFalse();
    }
}
