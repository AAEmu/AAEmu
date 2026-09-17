using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Pins <see cref="UnitFormulaKind"/> and <see cref="FormulaOwnerType"/> to the 10.0.2.13 content DB's
/// <c>enum_unit_formula_kinds</c> and <c>enum_unit_owner_types</c> (checked in as
/// <see cref="UnitFormulaKindContentSnapshot"/>).
/// </summary>
/// <remarks>
/// <c>FormulaManager.Load</c> seeds its per-owner table from the owner enum and drops any
/// <c>unit_formulas</c> row whose owner or kind the enums do not carry, so a missing member is silently
/// discarded content: 60 rows per owner exist for each of the eight owners, and the eight kinds above 46
/// plus the butler owner were being dropped before this batch.
/// </remarks>
public class UnitFormulaKindContentTests
{
    /// <summary>
    /// Members that deliberately do not spell their row the way the naming rule would. Everything else must
    /// be the literal PascalCase of its row's <c>name</c>, which is what makes a generated member checkable
    /// by eye.
    /// </summary>
    private static readonly Dictionary<uint, string> RenamedKindRows = new()
    {
        // enum_unit_formula_kinds spells 54 defence_dynamic_normalizable with the British s.
        [54] = "DefenceDynamicNormalizable"
    };

    private static Dictionary<uint, string> KindMembersById()
    {
        var members = new Dictionary<uint, string>();
        foreach (var value in Enum.GetValues<UnitFormulaKind>())
            members[(uint)value] = value.ToString();
        return members;
    }

    private static string PascalCase(string snakeCase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snakeCase);
        var parts = snakeCase.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    [Test]
    public async Task KindSnapshot_HoldsTheWholeTable()
    {
        var rows = UnitFormulaKindContentSnapshot.Kinds;
        var ids = rows.Select(row => row.Id).ToList();

        await Assert.That(rows.Length).IsEqualTo(60);
        await Assert.That(rows[0]).IsEqualTo((1u, "melee_critical"));
        await Assert.That(rows[^1]).IsEqualTo((68u, "casting_time_mul"));
        await Assert.That(ids.Distinct().Count()).IsEqualTo(rows.Length);
        // The id order is what lets a reviewer compare the enum and the table line by line.
        await Assert.That(ids.SequenceEqual(ids.Order())).IsTrue();
    }

    [Test]
    public async Task EveryKindIdHasAMember()
    {
        var members = KindMembersById();

        var missing = UnitFormulaKindContentSnapshot.Kinds
            .Where(row => !members.ContainsKey(row.Id))
            .Select(row => $"{row.Id} {row.Name}")
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryKindMemberNamesItsOwnRow()
    {
        var members = KindMembersById();

        var mismatched = UnitFormulaKindContentSnapshot.Kinds
            .Where(row => members[row.Id] != RenamedKindRows.GetValueOrDefault(row.Id, PascalCase(row.Name)))
            .Select(row => $"{row.Id}: row {row.Name}, member {members[row.Id]}")
            .ToList();

        await Assert.That(mismatched).IsEmpty();
    }

    [Test]
    public async Task NoKindMemberIsOutsideTheTable()
    {
        var rowIds = UnitFormulaKindContentSnapshot.Kinds.Select(row => row.Id).ToHashSet();

        var extra = KindMembersById().Keys.Where(id => !rowIds.Contains(id)).ToList();

        await Assert.That(extra).IsEmpty();
    }

    [Test]
    public async Task OwnerSnapshot_HoldsTheWholeTable()
    {
        var rows = UnitFormulaKindContentSnapshot.Owners;
        var ids = rows.Select(row => row.Id).ToList();

        await Assert.That(rows.Length).IsEqualTo(8);
        await Assert.That(rows[0]).IsEqualTo((0u, "character"));
        await Assert.That(rows[^1]).IsEqualTo((7u, "butler"));
        await Assert.That(ids.SequenceEqual(ids.Order())).IsTrue();
    }

    [Test]
    public async Task EveryOwnerIdHasAMemberAndEveryMemberOwnsARow()
    {
        var members = new Dictionary<uint, string>();
        foreach (var value in Enum.GetValues<FormulaOwnerType>())
            members[(uint)value] = value.ToString();

        var missing = UnitFormulaKindContentSnapshot.Owners
            .Where(row => !members.ContainsKey(row.Id) || members[row.Id] != PascalCase(row.Name))
            .Select(row => $"{row.Id} {row.Name}")
            .ToList();

        await Assert.That(missing).IsEmpty();
        await Assert.That(members.Count).IsEqualTo(UnitFormulaKindContentSnapshot.Owners.Length);
    }
}
