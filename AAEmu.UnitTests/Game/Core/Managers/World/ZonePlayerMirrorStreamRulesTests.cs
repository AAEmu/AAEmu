using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

public class ZonePlayerMirrorStreamRulesTests
{
    [Test]
    public async Task ShouldDropZoneMovementFor_DropsAResolvedCharacter()
    {
        var character = new Character(new UnitCustomModelParams());

        await Assert.That(ZonePlayerMirrorStreamRules.ShouldDropZoneMovementFor(character)).IsTrue();
    }

    [Test]
    public async Task ShouldDropZoneMovementFor_KeepsZoneOwnedUnits()
    {
        // NPCs (zone mirrors included), hulls and mates are simulated by the zone, so its stream is
        // their only source of movement.
        await Assert.That(ZonePlayerMirrorStreamRules.ShouldDropZoneMovementFor(new Npc())).IsFalse();
        await Assert.That(ZonePlayerMirrorStreamRules.ShouldDropZoneMovementFor(new Slave())).IsFalse();
        await Assert.That(ZonePlayerMirrorStreamRules.ShouldDropZoneMovementFor(new Mate())).IsFalse();
    }

    [Test]
    public async Task ShouldDropZoneMovementFor_KeepsAnIdThatResolvesToNothing()
    {
        // The relay resolves the id through the World's unit tables; an id that is not there is a
        // unit the World does not know at all, and dropping it would leave it frozen on every client.
        await Assert.That(ZonePlayerMirrorStreamRules.ShouldDropZoneMovementFor(null)).IsFalse();
    }
}
