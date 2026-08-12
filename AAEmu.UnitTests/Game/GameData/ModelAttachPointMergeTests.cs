using System.Numerics;

using AAEmu.Game.GameData;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// A model's attach helpers are gathered from several meshes, so the merge decides which definition wins.
/// It has to be a function of the input alone, not of the order rows happen to come back in.
/// </summary>
public class ModelAttachPointMergeTests
{
    [Test]
    public async Task MergesHelpersFromEveryMesh()
    {
        // The bug this guards: reading one element of a model resolved only the helpers in that mesh and
        // left the rest unresolved.
        var merged = ModelAttachPointGameData.MergeHelpers(
        [
            ("$cannon1", new Vector3(1f, 0f, 0f)),
            ("$cannon2", new Vector3(2f, 0f, 0f)),
            ("$heal_point0", new Vector3(3f, 0f, 0f))
        ], out var conflicts);

        await Assert.That(merged.Count).IsEqualTo(3);
        await Assert.That(merged["$cannon2"]).IsEqualTo(new Vector3(2f, 0f, 0f));
        await Assert.That(conflicts).IsEmpty();
    }

    [Test]
    public async Task HelperNamesAreCaseInsensitive()
    {
        var merged = ModelAttachPointGameData.MergeHelpers(
        [
            ("$Cannon1", new Vector3(1f, 0f, 0f)),
            ("$cannon1", new Vector3(1f, 0f, 0f))
        ], out var conflicts);

        await Assert.That(merged.Count).IsEqualTo(1);
        await Assert.That(conflicts).IsEmpty();
    }

    [Test]
    public async Task RepeatedHelperAtTheSamePosition_IsNotAConflict()
    {
        // Scenery meshes repeat across prefabs, so the same helper legitimately arrives more than once.
        var merged = ModelAttachPointGameData.MergeHelpers(
        [
            ("$cannon0", new Vector3(1f, 2f, 3f)),
            ("$cannon0", new Vector3(1f, 2f, 3f))
        ], out var conflicts);

        await Assert.That(merged.Count).IsEqualTo(1);
        await Assert.That(conflicts).IsEmpty();
    }

    [Test]
    public async Task ConflictingDefinition_IsReportedAndFirstWins()
    {
        var merged = ModelAttachPointGameData.MergeHelpers(
        [
            ("$cannon0", new Vector3(1f, 0f, 0f)),
            ("$cannon0", new Vector3(9f, 0f, 0f))
        ], out var conflicts);

        await Assert.That(merged["$cannon0"]).IsEqualTo(new Vector3(1f, 0f, 0f));
        await Assert.That(conflicts).IsEquivalentTo(new[] { "$cannon0" });
    }

    [Test]
    public async Task ConflictIsReportedOnce_HoweverManyTimesItRepeats()
    {
        var merged = ModelAttachPointGameData.MergeHelpers(
        [
            ("$cannon0", new Vector3(1f, 0f, 0f)),
            ("$cannon0", new Vector3(9f, 0f, 0f)),
            ("$cannon0", new Vector3(8f, 0f, 0f))
        ], out var conflicts);

        await Assert.That(merged.Count).IsEqualTo(1);
        await Assert.That(conflicts.Count).IsEqualTo(1);
    }

    [Test]
    public async Task OrderDecidesTheWinner_WhichIsWhyTheQueryIsOrdered()
    {
        // Documents the coupling: first-wins is only deterministic because the caller supplies a fixed
        // order. Reverse the input and the result changes, so the ORDER BY is load-bearing.
        (string, Vector3)[] candidates =
        [
            ("$cannon0", new Vector3(1f, 0f, 0f)),
            ("$cannon0", new Vector3(9f, 0f, 0f))
        ];

        var forward = ModelAttachPointGameData.MergeHelpers(candidates, out _);
        var reversed = ModelAttachPointGameData.MergeHelpers([.. candidates.Reverse()], out _);

        await Assert.That(forward["$cannon0"]).IsEqualTo(new Vector3(1f, 0f, 0f));
        await Assert.That(reversed["$cannon0"]).IsEqualTo(new Vector3(9f, 0f, 0f));
    }

    [Test]
    public async Task NoCandidates_ResolvesNothing()
    {
        var merged = ModelAttachPointGameData.MergeHelpers([], out var conflicts);

        await Assert.That(merged).IsEmpty();
        await Assert.That(conflicts).IsEmpty();
    }
}
