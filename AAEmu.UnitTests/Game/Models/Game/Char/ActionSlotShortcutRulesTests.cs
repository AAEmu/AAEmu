using AAEmu.Game.Models.Game.Char;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class ActionSlotShortcutRulesTests
{
    private static readonly DateTime ClearedAt = new(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(1u, 0u, true)]
    [Arguments(0u, 1u, true)]
    [Arguments(0u, 0u, false)]
    public async Task KeepsShortcut_IsASkillOrBuffItem(uint useSkillId, uint buffId, bool expected)
    {
        await Assert.That(ActionSlotShortcutRules.KeepsShortcut(useSkillId, buffId)).IsEqualTo(expected);
    }

    [Test]
    public async Task MoveInsideTheWindow_RestoresTheEmptySlot()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.Remember(3, ActionSlotType.ItemId, 9001, keepsShortcut: true, ClearedAt);

        var restores = memory.TakeRestores(9001, 45877, ClearedAt.AddMilliseconds(200));
        var slots = new ActionSlot[8];
        slots[3] = new ActionSlot { Type = ActionSlotType.None };

        await Assert.That(ActionSlotShortcutRules.ApplyRestore(slots, restores)).IsTrue();
        await Assert.That(slots[3].Type).IsEqualTo(ActionSlotType.ItemId);
        await Assert.That(slots[3].ActionId).IsEqualTo(9001ul);
    }

    [Test]
    public async Task ClearWithoutAMove_LeavesTheSlotEmpty()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.Remember(3, ActionSlotType.ItemId, 9001, keepsShortcut: true, ClearedAt);

        var restores = memory.TakeRestores(0, 0, ClearedAt.AddMilliseconds(200));
        var slots = new[] { new ActionSlot(), new ActionSlot(), new ActionSlot(), new ActionSlot { Type = ActionSlotType.None } };

        await Assert.That(ActionSlotShortcutRules.ApplyRestore(slots, restores)).IsFalse();
        await Assert.That(slots[3].Type).IsEqualTo(ActionSlotType.None);
    }

    [Test]
    public async Task PlainItem_IsNotRemembered()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.Remember(1, ActionSlotType.ItemId, 42, keepsShortcut: false, ClearedAt);

        await Assert.That(memory.TakeRestores(42, 10, ClearedAt).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ClearOlderThanTheWindow_IsNotRestored()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.Remember(2, ActionSlotType.ItemType, 45877, keepsShortcut: true, ClearedAt);

        var restores = memory.TakeRestores(1, 45877, ClearedAt.AddSeconds(2));

        await Assert.That(restores.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SlotFilledAfterTheClear_IsLeftAlone()
    {
        var restores = new List<ActionSlotShortcutRules.ClearedShortcut>
        {
            new(4, ActionSlotType.ItemId, 7, ClearedAt)
        };
        var slots = new ActionSlot[5];
        slots[4] = new ActionSlot { Type = ActionSlotType.Spell, ActionId = 99 };

        await Assert.That(ActionSlotShortcutRules.ApplyRestore(slots, restores)).IsFalse();
        await Assert.That(slots[4].Type).IsEqualTo(ActionSlotType.Spell);
        await Assert.That(slots[4].ActionId).IsEqualTo(99ul);
    }

    [Test]
    public async Task MoveNotedBeforeTheClear_MatchesThatItem()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.NoteMove(9001, 45877, ClearedAt);

        await Assert.That(memory.WasMoved(ActionSlotType.ItemId, 9001, ClearedAt.AddMilliseconds(100))).IsTrue();
        await Assert.That(memory.WasMoved(ActionSlotType.ItemType, 45877, ClearedAt.AddMilliseconds(100))).IsTrue();
        await Assert.That(memory.WasMoved(ActionSlotType.ItemId, 7, ClearedAt.AddMilliseconds(100))).IsFalse();
    }

    [Test]
    public async Task MoveOlderThanTheWindow_DoesNotMatch()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.NoteMove(9001, 45877, ClearedAt);

        await Assert.That(memory.WasMoved(ActionSlotType.ItemId, 9001, ClearedAt.AddSeconds(2))).IsFalse();
    }

    [Test]
    public async Task UseNotedBeforeTheClear_KeepsTheShortcut()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.NoteUse(9001, 45877, ClearedAt);

        await Assert.That(memory.WasUsed(ActionSlotType.ItemId, 9001, ClearedAt.AddMilliseconds(50))).IsTrue();
        await Assert.That(ActionSlotShortcutRules.ShouldKeepAgainstClear(
            keepsShortcut: true, usedRecently: true, movedRecently: false)).IsTrue();
        await Assert.That(ActionSlotShortcutRules.ShouldKeepAgainstClear(
            keepsShortcut: true, usedRecently: false, movedRecently: false)).IsFalse();
        await Assert.That(ActionSlotShortcutRules.ShouldKeepAgainstClear(
            keepsShortcut: false, usedRecently: true, movedRecently: false)).IsFalse();
        // Hotbar drag already placed the same item elsewhere: allow the source clear.
        await Assert.That(ActionSlotShortcutRules.ShouldKeepAgainstClear(
            keepsShortcut: true, usedRecently: true, movedRecently: false,
            sameItemAlreadyOnAnotherSlot: true)).IsFalse();
    }

    [Test]
    public async Task UseOlderThanTheProtectWindow_DoesNotMatch()
    {
        var memory = new ActionSlotShortcutMemory();
        memory.NoteUse(9001, 45877, ClearedAt);

        await Assert.That(memory.WasUsed(ActionSlotType.ItemId, 9001, ClearedAt.AddSeconds(6))).IsFalse();
    }
}
