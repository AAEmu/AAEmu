using System.Text;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Static;

/// <summary>
/// Pins every SkillResult byte to the client's result-to-symbol switch. The
/// client shows ui_texts key "skill_" + lower(symbol), so a member on the wrong byte shows another result's
/// message and nothing on the server notices.
/// </summary>
public class SkillResultWireTableTests
{
    private static IReadOnlyList<SkillResultClientTable.Row> Rows => SkillResultClientTable.Rows;

    /// <summary>The declared name for a row's byte. ToString() may answer "UrkStart" for 0x49; the alias is not the owner.</summary>
    private static string MemberName(SkillResultClientTable.Row row) =>
        Enum.GetNames<SkillResult>()
            .Single(name => (byte)Enum.Parse<SkillResult>(name) == row.Wire && name != nameof(SkillResult.UrkStart));

    /// <summary>The symbol a member name spells: one underscore per capital, "Urk" included as URK_.</summary>
    private static string SymbolFromName(string name)
    {
        var symbol = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                symbol.Append('_');
            symbol.Append(char.ToUpperInvariant(name[i]));
        }

        return symbol.ToString();
    }

    /// <summary>The client spells NOBUFF as one word where the member says NoBuff; underscores are not meaning.</summary>
    private static string Squash(string symbol) => symbol.Replace("_", "");

    [Test]
    public async Task Table_HasEveryCaseOfTheClientSwitch()
    {
        // Has 195 explicit cases between 0x00 and 0xCB; everything else is the default.
        await Assert.That(Rows.Count).IsEqualTo(195);
        await Assert.That(Rows.Select(r => r.Wire).Distinct().Count()).IsEqualTo(195);
        await Assert.That(Rows.Select(r => r.Member).Distinct().Count()).IsEqualTo(195);
        await Assert.That(Rows.Max(r => r.Wire)).IsEqualTo((byte)0xCB);
    }

    [Test]
    public async Task EveryMember_KeepsItsClientByte()
    {
        foreach (var row in Rows)
        {
            await Assert.That($"{MemberName(row)} = 0x{(byte)row.Member:X2}")
                .IsEqualTo($"{MemberName(row)} = 0x{row.Wire:X2}");
        }
    }

    [Test]
    public async Task EveryMemberName_SpellsTheSymbolTheClientReturnsForItsByte()
    {
        // A member on the right byte but carrying another result's name would still send the right message
        // while every server-side chooser reads the wrong meaning into it.
        foreach (var row in Rows)
        {
            var name = MemberName(row);

            await Assert.That($"{name} -> {Squash(SymbolFromName(name))}")
                .IsEqualTo($"{name} -> {Squash(row.Symbol)}");
        }
    }

    [Test]
    public async Task MembersWithoutAClientCase_AreTheKnownFour()
    {
        // UrkStart is an alias of 0x49 and so counts as pinned; the four below are the only bytes the switch
        // has no case for, each justified on its member.
        var pinned = Rows.Select(r => r.Wire).ToHashSet();
        var unpinned = Enum.GetNames<SkillResult>()
            .Where(name => !pinned.Contains((byte)Enum.Parse<SkillResult>(name)))
            .OrderBy(name => (byte)Enum.Parse<SkillResult>(name));

        await Assert.That(string.Join(", ", unpinned))
            .IsEqualTo("UrkUnknown, UrkSkillCooldown, UrkEmptySlotInventory, UrkCombatResource");
    }

    [Test]
    public async Task UrkUnknown_SitsOnTheByteTheDisplayPathSwallows()
    {
        // 0x40 has no case in and the display path returns for it before any message
        // is built, so the fail-closed result shows nothing at all rather than a wrong text.
        await Assert.That((byte)SkillResult.UrkUnknown).IsEqualTo((byte)0x40);
        await Assert.That(SkillResultClientTable.SymbolFor(0x40)).IsEqualTo("URK_UNKNOWN");
    }

    [Test]
    public async Task SkillCooldown_UsesTheByteTheKind102EvaluatorWrites()
    {
        // Writes 0xAC on a failed cooldown check; the symbol switch has no case for it.
        await Assert.That((byte)SkillResult.UrkSkillCooldown).IsEqualTo((byte)0xAC);
        await Assert.That(SkillResultClientTable.SymbolFor(0xAC)).IsEqualTo("URK_UNKNOWN");
    }

    [Test]
    public async Task EmptySlotInventory_UsesTheByteTheKind106EvaluatorWrites()
    {
        // Writes 0xB0 with detail 0x19 (BAG_FULL); the detail carries the message, not the byte.
        await Assert.That((byte)SkillResult.UrkEmptySlotInventory).IsEqualTo((byte)0xB0);
        await Assert.That(SkillResultClientTable.SymbolFor(0xB0)).IsEqualTo("URK_UNKNOWN");
    }

    [Test]
    public async Task CombatResource_UsesTheByteTheKind136EvaluatorWrites()
    {
        // Writes 199 (0xC7) when the combat resource check fails.
        await Assert.That((byte)SkillResult.UrkCombatResource).IsEqualTo((byte)0xC7);
        await Assert.That(SkillResultClientTable.SymbolFor(0xC7)).IsEqualTo("URK_UNKNOWN");
    }

    [Test]
    public async Task UrkStart_IsTheLastByteBelowTheDisplayGate()
    {
        // Suppresses results above 0x49 when the display flag is false: 0x49 is the last plain
        // result and 0x4A (URK_LEVEL) the first requirement result.
        await Assert.That((byte)SkillResult.UrkStart).IsEqualTo((byte)0x49);
        await Assert.That(SkillResult.UrkStart).IsEqualTo(SkillResult.SourceCannotUseWhileLevitating);
        await Assert.That((byte)SkillResult.UrkLevel).IsEqualTo((byte)0x4A);
    }

    [Test]
    public async Task UnassignedBytes_HaveNoMember()
    {
        // The switch skips these; a member placed on one of them would show "URK_UNKNOWN".
        foreach (var wire in new byte[] { 0x87, 0x95, 0xA1, 0xA2, 0xA3 })
        {
            await Assert.That(Enum.IsDefined((SkillResult)wire)).IsFalse();
        }
    }

    [Test]
    public async Task NoTwoMembers_ShareAByte_ExceptTheUrkStartAlias()
    {
        var shared = Enum.GetNames<SkillResult>()
            .Where(name => name != nameof(SkillResult.UrkStart))
            .GroupBy(name => (byte)Enum.Parse<SkillResult>(name))
            .Where(g => g.Count() > 1)
            .Select(g => $"0x{g.Key:X2}: {string.Join(", ", g)}")
            .ToList();

        await Assert.That(shared).IsEmpty();
    }
}
