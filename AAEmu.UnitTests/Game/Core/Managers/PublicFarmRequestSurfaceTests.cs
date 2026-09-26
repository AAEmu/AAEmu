using System.Reflection;

using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// Pins the public farm <b>request surface</b> closed, after CS 0x163 was found to delete every crop
/// the caller had planted and answer nothing.
/// </summary>
/// <remarks>
/// <para>
/// The defect was not a bug in a rule — it was that the farm manager exposed a bulk delete and a
/// planted count at all, which made a plausible reading of an editor-only opcode into a destructive
/// one. Retuning that behaviour would leave the same trap in place for the next reader, so this
/// test pins the <i>absence</i> instead: the interface must carry no member that can remove a
/// character's farm doodads, and no member that reports a planted count for a capacity comparison.
/// </para>
/// <para>
/// A test asserting a member is missing is unusual and is deliberate here. Every test that was
/// written against the old behaviour had to be deleted, because the behaviour it described is
/// exactly what is being withdrawn; this is the one assertion that can outlive them. Re-adding a bulk
/// delete to this interface fails here even if no packet calls it yet.
/// </para>
/// </remarks>
public class PublicFarmRequestSurfaceTests
{
    private static readonly string[] Allowed = ["PublicFarmTick", "InPublicFarm", "GetFarmType"];

    [Test]
    public async Task TheFarmManagerExposesNoBulkDeleteOrPlantedCount()
    {
        // Walk the concrete set rather than spot-checking names, so a rename cannot dodge the guard
        // and a new member cannot slip in beside an unchecked one.
        var declared = typeof(IPublicFarmManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(declared).IsEquivalentTo(Allowed.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Test]
    public async Task NoPublicFarmManagerMemberTakesACharacterAndReturnsAnInt()
    {
        // The shape of the removed API: (Character, ...) -> int is what "how many did I delete"
        // looks like. Asserting on the shape catches a renamed rebuild of the same hazard.
        var offenders = typeof(IPublicFarmManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(int))
            .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }
}
