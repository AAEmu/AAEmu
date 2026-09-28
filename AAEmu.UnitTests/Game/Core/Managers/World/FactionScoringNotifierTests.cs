using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public sealed class FactionScoringNotifierTests
{
    [Test]
    public async Task ZoneScoreChangeIsPublishedAsItsKindAndAppliedDelta()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreChange(new ZoneScoreApplication(
            4, 5, 60, 70, 500, 10, 1, 1, false, true));

        var body = ((SCZoneScoreUpdatePacket)sent[0]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(4u);
        expected.Write(10);

        await Assert.That(sent).HasCount(1);
        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task AChangeThatCreditedNothingPublishesNothing()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreChange(new ZoneScoreApplication(
            4, 5, 70, 70, 500, 0, 1, 1, false, true));

        await Assert.That(sent).IsEmpty();
    }

    [Test]
    public async Task AResetIsPublishedEvenWhenTheScoreWasAlreadyZero()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreReset(4);

        var body = ((SCZoneScoreResetPacket)sent[0]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(4u);

        await Assert.That(sent).HasCount(1);
        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task CompetitionPointIsPublishedAsItsCompetitionFactionAndDelta()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishCompetitionPoint(new FactionCompetitionScoreApplication(
            12, 149, 300, 350, 50, 50));

        var body = ((SCFactionCompetitionUpdatePointPacket)sent[0]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write((ushort)12);
        expected.Write(149u);
        expected.Write(50);

        await Assert.That(sent).HasCount(1);
        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task ACompetitionChangeThatCreditedNothingPublishesNothing()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishCompetitionPoint(new FactionCompetitionScoreApplication(
            12, 149, 300, 300, 0, 0));

        await Assert.That(sent).IsEmpty();
    }

    [Test]
    public async Task AListSenderRequiresTheCatalogItOrdersBy()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        // Ordering comes from the catalog, so a caller that cannot supply it is refused rather
        // than silently falling back to some other order.
        await Assert.That(() => notifier.PublishZoneScoreList(null, []))
            .Throws<ArgumentNullException>();
        await Assert.That(() => notifier.PublishZoneScoreList(null, null))
            .Throws<ArgumentNullException>();
        await Assert.That(sent).IsEmpty();
    }

    [Test]
    public async Task AFailedSendIsLoggedAndDoesNotStopTheNextChange()
    {
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(packet =>
        {
            sent.Add(packet);
            throw new InvalidOperationException("send failed");
        });

        notifier.PublishZoneScoreChange(new ZoneScoreApplication(4, 5, 0, 10, 10, 10, 0, 1, true, false));
        notifier.PublishZoneScoreChange(new ZoneScoreApplication(6, 5, 0, 20, 20, 20, 0, 1, true, false));

        await Assert.That(sent).HasCount(2);
    }
}
