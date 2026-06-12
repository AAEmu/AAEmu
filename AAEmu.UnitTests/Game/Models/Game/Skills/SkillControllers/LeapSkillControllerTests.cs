using System.Numerics;
using AAEmu.Game.Models.Game.Skills.SkillControllers;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.SkillControllers;

public class LeapSkillControllerTests
{
    [Test]
    public async Task ApplyDirectionConstraint_Both_AlwaysReturnsCandidate()
    {
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(20, 0, 0); // past target

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.Both, owner, candidate, target);

        await Assert.That(result).IsEqualTo(candidate);
    }

    [Test]
    public async Task ApplyDirectionConstraint_ForwardOnly_AllowedWhenLandingCloserToTarget()
    {
        // Owner 10m from target. Candidate 3m from target (forward leap toward target).
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(7, 0, 0);

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.ForwardOnly, owner, candidate, target);

        await Assert.That(result).IsEqualTo(candidate);
    }

    [Test]
    public async Task ApplyDirectionConstraint_ForwardOnly_CollapsesToOwnerWhenLandingFarther()
    {
        // ForwardOnly leap that would land farther from target than owner started.
        // This happens when sign of DistanceOffset is wrong.
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(-5, 0, 0); // 15m from target — farther than owner

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.ForwardOnly, owner, candidate, target);

        await Assert.That(result).IsEqualTo(owner);
    }

    [Test]
    public async Task ApplyDirectionConstraint_BackwardOnly_AllowedWhenLandingFarther()
    {
        // BackwardOnly: leap AWAY from target. Landing farther is correct.
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(-3, 0, 0); // 13m from target — farther

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.BackwardOnly, owner, candidate, target);

        await Assert.That(result).IsEqualTo(candidate);
    }

    [Test]
    public async Task ApplyDirectionConstraint_BackwardOnly_CollapsesToOwnerWhenLandingCloser()
    {
        // BackwardOnly leap that would land closer to target is rejected.
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(5, 0, 0); // 5m from target — closer than owner

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.BackwardOnly, owner, candidate, target);

        await Assert.That(result).IsEqualTo(owner);
    }

    [Test]
    public async Task ApplyDirectionConstraint_ForwardOnly_BoundaryDistance_AllowsCandidate()
    {
        // Candidate exactly the same distance as owner — ForwardOnly allows equal (not >).
        var owner = new Vector3(0, 0, 0);
        var target = new Vector3(10, 0, 0);
        var candidate = new Vector3(20, 0, 0); // 10m from target, same as owner

        var result = LeapSkillController.ApplyDirectionConstraint(
            LeapSkillController.LeapDirection.ForwardOnly, owner, candidate, target);

        await Assert.That(result).IsEqualTo(candidate);
    }
}
