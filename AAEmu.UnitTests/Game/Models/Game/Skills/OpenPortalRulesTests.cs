using System.Numerics;

using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The portal-open distance is a radius around the owner, not a per-axis comparison. The old check
/// compared only the +X and +Y sides (<c>portalInfo.X &gt; owner.X + Distance || portalInfo.Y &gt;
/// owner.Y + Distance</c>), so a portal placed to the west or south of its owner was accepted
/// whatever the gap, and a portal placed diagonally was accepted at 1.41 x the distance in the
/// corner of the square.
/// </summary>
/// <remarks>content: all 11 rows of open_portal_effects use distance 3.0.</remarks>
public class OpenPortalRulesTests
{
    private const float Distance = 3f;
    private static readonly Vector3 Owner = new(1000f, 1000f, 100f);

    [Test]
    public async Task WithinDistance_AcceptsTheOwnersOwnPosition()
    {
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner, Owner, Distance)).IsTrue();
    }

    [Test]
    public async Task WithinDistance_AcceptsEveryDirectionInsideTheRadius()
    {
        // 2.9 on any single axis, and 2.1 on both (2.97 away).
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(2.9f, 0f, 0f), Owner, Distance)).IsTrue();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner - new Vector3(2.9f, 0f, 0f), Owner, Distance)).IsTrue();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(0f, 2.9f, 0f), Owner, Distance)).IsTrue();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner - new Vector3(0f, 2.9f, 0f), Owner, Distance)).IsTrue();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(2.1f, 2.1f, 0f), Owner, Distance)).IsTrue();
    }

    [Test]
    public async Task WithinDistance_RejectsAPortalBehindItsOwner()
    {
        // The direction the old comparison never looked at: a portal 100 units west or south passed.
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner - new Vector3(100f, 0f, 0f), Owner, Distance)).IsFalse();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner - new Vector3(0f, 100f, 0f), Owner, Distance)).IsFalse();
    }

    [Test]
    public async Task WithinDistance_RejectsTheCornerBeyondTheRadius()
    {
        // 2.5 on both axes is 3.54 away — each axis is inside 3, the portal is not.
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(2.5f, 2.5f, 0f), Owner, Distance)).IsFalse();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner - new Vector3(2.5f, 2.5f, 0f), Owner, Distance)).IsFalse();
    }

    [Test]
    public async Task WithinDistance_StopsAtTheRadius()
    {
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(Distance, 0f, 0f), Owner, Distance)).IsTrue();
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(Distance + 0.01f, 0f, 0f), Owner, Distance)).IsFalse();
    }

    [Test]
    public async Task WithinDistance_IgnoresHeight()
    {
        // The measure is the ground-plane one the rest of the skill code uses: the effect never passed
        // includeZAxis, and a portal is placed on the ground under the owner either way.
        await Assert.That(OpenPortalRules.IsWithinOpenDistance(Owner + new Vector3(0f, 0f, 100f), Owner, Distance)).IsTrue();
    }
}
