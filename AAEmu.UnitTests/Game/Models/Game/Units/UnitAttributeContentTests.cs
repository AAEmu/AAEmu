using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// Pins <see cref="UnitAttribute"/> to the 10.0.2.13 content DB's <c>enum_unit_attribute</c> table
/// (checked in as <see cref="UnitAttributeContentSnapshot"/>).
/// </summary>
public class UnitAttributeContentTests
{
    /// <summary>
    /// Members that deliberately do not spell their row the way the naming rule would. Everything else
    /// must be the literal PascalCase of its row's <c>name</c>, which is what makes a generated member
    /// checkable by eye.
    /// </summary>
    private static readonly Dictionary<uint, string> RenamedRows = new()
    {
        // enum_unit_attribute names 187 physics_collision_front_damage_mul; the member is the
        // whole-hull value and has carried this name since before the row was renamed.
        [187] = "PhysicsCollisionDamageMul",
        // enum_unit_attribute spells 239 slave_vehicle_engin_power.
        [239] = "SlaveVehicleEnginePower"
    };

    /// <summary>Ids the 10.0.2.13 table does not carry that this server still declares.</summary>
    private static readonly uint[] DroppedButDeclared =
    [
        20, 21, 59, 60, 65, 79, 80, 84, 85, 126, 127
    ];

    /// <summary>Of those, the ones no content and no code can produce.</summary>
    private static readonly uint[] DroppedAndObsolete =
    [
        20, 59, 60, 65, 79, 80, 84, 85, 126
    ];

    private static Dictionary<uint, string> MembersById()
    {
        var members = new Dictionary<uint, string>();
        foreach (var value in Enum.GetValues<UnitAttribute>())
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
    public async Task Snapshot_HoldsTheWholeTable()
    {
        var rows = UnitAttributeContentSnapshot.Rows;
        var ids = rows.Select(row => row.Id).ToList();

        await Assert.That(rows.Length).IsEqualTo(255);
        await Assert.That(rows[0]).IsEqualTo((0u, "str"));
        await Assert.That(rows[^1]).IsEqualTo((289u, "extra_gain_by_mine_act_group"));
        await Assert.That(ids.Distinct().Count()).IsEqualTo(rows.Length);
        // The id order is what lets a reviewer compare the enum and the table line by line.
        await Assert.That(ids.SequenceEqual(ids.Order())).IsTrue();
    }

    [Test]
    public async Task EveryContentIdHasAMember()
    {
        var members = MembersById();

        var missing = UnitAttributeContentSnapshot.Rows
            .Where(row => !members.ContainsKey(row.Id))
            .Select(row => $"{row.Id} {row.Name}")
            .ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task EveryMemberNamesItsOwnRow()
    {
        var members = MembersById();

        var mismatched = UnitAttributeContentSnapshot.Rows
            .Where(row => members[row.Id] != RenamedRows.GetValueOrDefault(row.Id, PascalCase(row.Name)))
            .Select(row => $"{row.Id}: row {row.Name}, member {members[row.Id]}")
            .ToList();

        await Assert.That(mismatched).IsEmpty();
    }

    [Test]
    public async Task NoTwoRowsShareAMember()
    {
        var members = MembersById();
        var named = UnitAttributeContentSnapshot.Rows.Select(row => members[row.Id]).ToList();

        await Assert.That(named.Distinct().Count()).IsEqualTo(named.Count);
    }

    [Test]
    public async Task DroppedIds_AreStillDeclared()
    {
        var members = MembersById();

        var missing = DroppedButDeclared.Where(id => !members.ContainsKey(id)).ToList();

        await Assert.That(missing).IsEmpty();
    }

    [Test]
    public async Task DroppedIds_AreNotInTheSnapshot()
    {
        var snapshotIds = UnitAttributeContentSnapshot.Rows.Select(row => row.Id).ToHashSet();

        var present = DroppedButDeclared.Where(snapshotIds.Contains).ToList();

        await Assert.That(present).IsEmpty();
    }

    [Test]
    public async Task DroppedIds_AreSplitIntoObsoleteAndLive()
    {
        var enumType = typeof(UnitAttribute);

        var notObsolete = DroppedAndObsolete
            .Where(id => enumType.GetField(Enum.GetName(enumType, id))
                .GetCustomAttributes(typeof(ObsoleteAttribute), false).Length != 1)
            .ToList();
        await Assert.That(notObsolete).IsEmpty();

        // 21 is live: unit_attribute_limits row 8 bounds it. 127 is live: ExpeditionBuffGameData hands
        // it out for expedition buff 10. Obsoleting either would hide a working id.
        var liveButObsolete = new[] { UnitAttribute.MeleeBlock, UnitAttribute.HonorPointGainBattleFieldMul }
            .Where(value => enumType.GetField(value.ToString())
                .GetCustomAttributes(typeof(ObsoleteAttribute), false).Length != 0)
            .ToList();
        await Assert.That(liveButObsolete).IsEmpty();
    }

    [Test]
    public async Task Id14_HasNeitherRowNorMember()
    {
        // Two unit_modifiers rows (buffs 185/186) name id 14 and enum_unit_attribute has no name for
        // it, so there is nothing to derive a member from. It stays unnamed and the loaders report it.
        await Assert.That(UnitAttributeContentSnapshot.Rows.Any(row => row.Id == 14)).IsFalse();
        await Assert.That(MembersById().ContainsKey(14)).IsFalse();
    }

    [Test]
    public async Task LanguageLadders_AreMembersNow()
    {
        // 10.0.2's actability_groups.unit_attr_id names 158-165, which used to sit in a comment block.
        var members = MembersById();

        var missing = Enumerable.Range(158, 8).Select(id => (uint)id).Where(id => !members.ContainsKey(id)).ToList();

        await Assert.That(missing).IsEmpty();
    }
}
