using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterRepairRulesTests
{
    [Test]
    public async Task BagRepair_NeedsTheFeatureAndAPaidPatron()
    {
        await Assert.That(CharacterRepairRules.CanRepairWithoutBlacksmith(true, true)).IsTrue();
        await Assert.That(CharacterRepairRules.CanRepairWithoutBlacksmith(true, false)).IsFalse();
        await Assert.That(CharacterRepairRules.CanRepairWithoutBlacksmith(false, true)).IsFalse();
        await Assert.That(CharacterRepairRules.CanRepairWithoutBlacksmith(false, false)).IsFalse();
    }

    [Test]
    public async Task NpcRepair_RequiresARepairman()
    {
        var repairman = new NpcTemplate { Repairman = true };
        var blacksmith = new NpcTemplate { Blacksmith = true };
        await Assert.That(CharacterRepairRules.CanRepairAtNpc(repairman)).IsTrue();
        await Assert.That(CharacterRepairRules.CanRepairAtNpc(blacksmith)).IsFalse();
        await Assert.That(CharacterRepairRules.CanRepairAtNpc(null)).IsFalse();
    }

    [Test]
    public async Task NeedsRepair_RefusesZeroMaxOrFull()
    {
        var empty = new EquipItem { Durability = 10 };
        await Assert.That(empty.MaxDurability).IsEqualTo((byte)0);
        await Assert.That(CharacterRepairRules.NeedsRepair(empty)).IsFalse();
        await Assert.That(CharacterRepairRules.TryRestore(empty)).IsFalse();
        await Assert.That(empty.Durability).IsEqualTo((byte)10);

        await Assert.That(CharacterRepairRules.NeedsRepair(null)).IsFalse();
    }

    [Test]
    public async Task TryRestore_FillsWornDurabilityAndLeavesFullAlone()
    {
        var worn = new RepairablePiece(100, 40);
        await Assert.That(CharacterRepairRules.NeedsRepair(worn)).IsTrue();
        await Assert.That(CharacterRepairRules.TryRestore(worn)).IsTrue();
        await Assert.That(worn.Durability).IsEqualTo((byte)100);
        await Assert.That(worn.IsDirty).IsTrue();
        await Assert.That(CharacterRepairRules.TryRestore(worn)).IsFalse();

        var full = new RepairablePiece(80, 80);
        await Assert.That(CharacterRepairRules.NeedsRepair(full)).IsFalse();
        await Assert.That(CharacterRepairRules.TryRestore(full)).IsFalse();
        await Assert.That(full.Durability).IsEqualTo((byte)80);
    }

    private sealed class RepairablePiece : EquipItem
    {
        private readonly byte _max;

        public RepairablePiece(byte max, byte current)
        {
            Durability = current;
            _max = max;
        }

        public override byte MaxDurability => _max;
    }
}
