using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitCustomModelParamsTests
{
    [Test]
    public async Task ClearUnusedVisualRaceOverride_ZerosCopyOfOriginRace()
    {
        var appearance = new UnitCustomModelParams(UnitCustomModelType.Hair)
        {
            Race = 4,
            Gender = 2,
            VisualRace = 4,
            VisualGender = 2,
            VisualRaceExpiredTime = 1
        };

        appearance.ClearUnusedVisualRaceOverride(4);

        await Assert.That(appearance.VisualRace).IsEqualTo((byte)0);
        await Assert.That(appearance.VisualGender).IsEqualTo((byte)0);
        await Assert.That(appearance.VisualRaceExpiredTime).IsEqualTo(0);
        await Assert.That(appearance.Race).IsEqualTo((byte)4);
    }

    [Test]
    public async Task ClearUnusedVisualRaceOverride_KeepsRealRaceChange()
    {
        var appearance = new UnitCustomModelParams(UnitCustomModelType.Hair)
        {
            Race = 4,
            Gender = 2,
            VisualRace = 1,
            VisualGender = 1,
            VisualRaceExpiredTime = 99
        };

        appearance.ClearUnusedVisualRaceOverride(4);

        await Assert.That(appearance.VisualRace).IsEqualTo((byte)1);
        await Assert.That(appearance.VisualGender).IsEqualTo((byte)1);
        await Assert.That(appearance.VisualRaceExpiredTime).IsEqualTo(99);
    }

    [Test]
    public async Task Write_AfterClear_EmitsZeroVisualRace()
    {
        var appearance = new UnitCustomModelParams(UnitCustomModelType.Hair)
        {
            Race = 4,
            Gender = 2,
            VisualRace = 4,
            VisualGender = 2
        };
        appearance.ClearUnusedVisualRaceOverride(4);

        var written = appearance.Write(new PacketStream());
        var roundTrip = new UnitCustomModelParams();
        roundTrip.Read(new PacketStream(written.GetBytes()));

        await Assert.That(roundTrip.Race).IsEqualTo((byte)4);
        await Assert.That(roundTrip.VisualRace).IsEqualTo((byte)0);
        await Assert.That(roundTrip.VisualGender).IsEqualTo((byte)0);
    }
}
