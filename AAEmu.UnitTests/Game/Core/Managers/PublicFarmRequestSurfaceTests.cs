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
/// test pins the <i>shape</i> of what the manager may expose instead of only the names.
/// </para>
/// <para>
/// The interface has since grown the two members the row needs — an area lookup and a removal — and
/// both assertions below still hold: neither reports a planted count, and neither removes more than
/// one crop. A test asserting a member is missing is unusual and is deliberate here; these two
/// survived the growth of the surface, which is exactly what they are for.
/// </para>
/// </remarks>
public class PublicFarmRequestSurfaceTests
{
    private static readonly string[] Allowed =
        ["GetFarmArea", "GetFarmType", "InPublicFarm", "PublicFarmTick", "RemoveCrop"];

    [Test]
    public async Task TheFarmManagerExposesOnlyTheNamedOperations()
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
    public async Task NoPublicFarmManagerMemberReportsAPlantedCount()
    {
        // The shape of the removed API: (Character, ...) -> int is what "how many did I delete" looks
        // like. Asserting on the shape catches a renamed rebuild of the same hazard, and an int-typed
        // member here would invite a count comparison against a capacity the content may not have.
        var offenders = typeof(IPublicFarmManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(int))
            .Select(m => $"{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})")
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task NoPublicFarmManagerMemberTakesACollectionOfDoodads()
    {
        // The bulk-delete shape. A removal here takes one crop and says why it did not remove it, so
        // no member may accept a list, an array or any other enumerable of doodads: that is the one
        // parameter shape that turns a single removal into "drop everything this player planted".
        var offenders = typeof(IPublicFarmManager)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters()
                .Where(p => p.ParameterType != typeof(string) && typeof(System.Collections.IEnumerable)
                    .IsAssignableFrom(p.ParameterType))
                .Select(p => $"{m.Name}({p.ParameterType.Name} {p.Name})"))
            .ToArray();

        await Assert.That(offenders).IsEmpty();
    }

    [Test]
    public async Task TheRemovalTakesExactlyOneDoodad()
    {
        // Pinned on the concrete member rather than as a name in the allow list, so the guarantee is
        // about what it can destroy: one crop per call, and a caller that wants the rest asks again.
        var removal = typeof(IPublicFarmManager)
            .GetMethod(nameof(IPublicFarmManager.RemoveCrop), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("The farm manager exposes no removal.");

        var doodadParameters = removal.GetParameters()
            .Count(p => typeof(AAEmu.Game.Models.Game.DoodadObj.Doodad).IsAssignableFrom(p.ParameterType));

        await Assert.That(doodadParameters).IsEqualTo(1);
    }
}
