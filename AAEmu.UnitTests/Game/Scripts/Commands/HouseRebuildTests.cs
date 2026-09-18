using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

public class HouseRebuildTests
{
    /// <summary>
    /// The placement path answers nothing back, so the command counts the houses of that design the caller
    /// owns before and after it runs. Anything else must not read as "the placement worked": another player's
    /// house of the same design, or the caller's house of a different design.
    /// </summary>
    [Test]
    public async Task CountOwnHousesOfDesign_CountsOnlyTheCallersHousesOfThatDesign()
    {
        House[] houses =
        [
            NewHouse(1, designId: 432, ownerId: 7),
            NewHouse(2, designId: 432, ownerId: 7),
            NewHouse(3, designId: 432, ownerId: 8),
            NewHouse(4, designId: 851, ownerId: 7)
        ];

        await Assert.That(HouseRebuild.CountOwnHousesOfDesign(432, 7, houses)).IsEqualTo(2);
        await Assert.That(HouseRebuild.CountOwnHousesOfDesign(851, 7, houses)).IsEqualTo(1);
        await Assert.That(HouseRebuild.CountOwnHousesOfDesign(432, 8, houses)).IsEqualTo(1);
        await Assert.That(HouseRebuild.CountOwnHousesOfDesign(432, 9, houses)).IsEqualTo(0);
    }

    /// <summary>
    /// An empty world is the case the subcommand exists for: a server with no player houses, where the caller
    /// owns nothing of that design yet and the placement is the only thing that can change the count.
    /// </summary>
    [Test]
    public async Task CountOwnHousesOfDesign_WithNoHouses_IsZero()
    {
        await Assert.That(HouseRebuild.CountOwnHousesOfDesign(432, 7, [])).IsEqualTo(0);
    }

    private static House NewHouse(uint id, uint designId, uint ownerId) =>
        new()
        {
            Id = id,
            TemplateId = designId,
            OwnerId = ownerId
        };
}
