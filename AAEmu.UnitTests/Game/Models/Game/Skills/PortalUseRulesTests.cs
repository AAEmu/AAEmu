using System.Numerics;

using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Pins the reachability contract the portal use guard relies on: a portal is usable when it lies in
/// the region neighbourhood the character can see the world from, and not otherwise. These cover the
/// rule alone; <c>PortalUseProximityTests</c> covers the manager calling it.
/// </summary>
public class PortalUseRulesTests
{
    /// <summary>WorldManager.REGION_SIZE.</summary>
    private const float RegionSize = 64f;

    /// <summary>WorldManager.REGION_NEIGHBORHOOD_SIZE.</summary>
    private const int Neighborhood = 2;

    private static readonly Vector3 CharacterPosition = new(100f, 100f, 0f);

    private static GameObject PlaceCharacter(float x, float y, float z, bool withRegion = true)
    {
        var character = new GameObject();
        character.Transform.Local.SetPosition(x, y, z);
        if (withRegion)
            character.Region = new Region(null, (int)(x / RegionSize), (int)(y / RegionSize), 0);
        return character;
    }

    [Test]
    public async Task Reachable_AcceptsThePortalTheCharacterStandsOn()
    {
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z);

        await Assert.That(PortalUseRules.IsReachableFrom(character, CharacterPosition)).IsTrue();
    }

    [Test]
    public async Task Reachable_AcceptsAPortalInsideTheNeighbourhood()
    {
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z);
        var reach = RegionSize * Neighborhood;

        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition + new Vector3(reach, 0f, 0f))).IsTrue();
        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition - new Vector3(0f, reach, 0f))).IsTrue();
    }

    [Test]
    public async Task Reachable_RefusesAPortalBeyondTheNeighbourhood()
    {
        // One region past the neighbourhood: the client could not have collided with it.
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z);
        var beyond = RegionSize * (Neighborhood + 1);

        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition + new Vector3(beyond, 0f, 0f))).IsFalse();
        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition + new Vector3(0f, beyond, 0f))).IsFalse();
    }

    [Test]
    public async Task Reachable_IsWiderOnTheNegativeSideThanThePositiveOneAtTheSameDistance()
    {
        // The cell index is an integer division of the world coordinate, so it truncates towards zero
        // and a coordinate below the origin lands one cell higher than the same distance above it. At
        // 192 units the positive side is cell 4 and already outside the neighbourhood of cell 1, while
        // the negative side is only cell -1 and still inside it. That is the shared seam's behaviour,
        // not this guard's, and it is recorded here rather than left to be rediscovered.
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z);
        var sameDistance = RegionSize * (Neighborhood + 1);

        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition + new Vector3(sameDistance, 0f, 0f))).IsFalse();
        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition - new Vector3(sameDistance, 0f, 0f))).IsTrue();
    }

    [Test]
    public async Task Reachable_RefusesACharacterTheWorldHasNotPlaced()
    {
        // An unplaced character has no neighbourhood to be inside of, so the guard refuses rather than
        // treating "unknown" as "close".
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z,
            withRegion: false);

        await Assert.That(PortalUseRules.IsReachableFrom(character, CharacterPosition)).IsFalse();
    }

    [Test]
    public async Task Reachable_IgnoresHeight()
    {
        // The neighbourhood is a ground-plane grid, so a portal far above or below is still the one the
        // character collided with; the client's walk-in has no vertical component.
        var character = PlaceCharacter(CharacterPosition.X, CharacterPosition.Y, CharacterPosition.Z);

        await Assert.That(PortalUseRules.IsReachableFrom(
            character, CharacterPosition + new Vector3(0f, 0f, 100f))).IsTrue();
    }
}
