using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// Equip slot reinforcement: read a character's progress, or set one slot's level and bar so the
/// client's window can be driven without having to feed a ladder up from zero first.
/// </summary>
public class EquipSlotReinforceCommand : ICommand
{
    public string[] CommandNames { get; set; } = ["reinforce", "esr"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<list|set|roll|apply> [slot] [level] [exp]";
    }

    public string GetCommandHelpText()
    {
        return "reinforce list - every reinforcement slot and this character's progress on it.\n" +
               "reinforce set <slot> <level> [exp] - set a slot's level and bar.\n" +
               "reinforce roll <slot> [tier] - re-roll an effect the slot already obtained (its highest tier by default).\n" +
               "reinforce apply <slot> <tier> <on|off> - switch an obtained effect on or off, as the window's radio does.";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "list";

        switch (action)
        {
            case "list":
                List(character, messageOutput);
                break;
            case "set":
                Set(character, args, messageOutput);
                break;
            case "roll":
                Roll(character, args, messageOutput);
                break;
            case "apply":
                Apply(character, args, messageOutput);
                break;
            default:
                messageOutput.SendMessage($"[{CommandNames[0]}] {GetCommandLineHelp()}");
                break;
        }
    }

    private void List(Character character, IMessageOutput messageOutput)
    {
        var data = EquipSlotReinforceGameData.Instance;
        foreach (var slotTypeId in data.SlotTypeIds.OrderBy(slot => slot))
        {
            var state = character.EquipSlotReinforces.StateOf(slotTypeId);
            var ladder = data.Ladder(slotTypeId);
            var attribute = data.AttributeOf(slotTypeId);
            messageOutput.SendMessage(
                $"slot {slotTypeId} ({attribute}) level {state?.Level ?? 0}/{ladder[^1].Level} exp {state?.Exp ?? 0}");
        }

        foreach (var effect in character.EquipSlotReinforces.Effects)
        {
            var tier = data.GetLevelEffectById(effect.LevelEffectId);
            var modifier = data.GetUnitModifierById(effect.UnitModifierId);
            messageOutput.SendMessage(
                $"slot {effect.SlotTypeId} tier {effect.LevelEffectId} (level {tier?.TriggerLevel}) " +
                $"{(effect.Applied ? "on" : "off")} -> modifier {effect.UnitModifierId} " +
                $"({(UnitAttribute?)modifier?.UnitAttributeId} {modifier?.Value})");
        }

        messageOutput.SendMessage(
            $"totals: offence {character.EquipSlotReinforces.AttributeTotal(EquipSlotReinforceAttribute.Offence)}, " +
            $"defence {character.EquipSlotReinforces.AttributeTotal(EquipSlotReinforceAttribute.Defence)}, " +
            $"support {character.EquipSlotReinforces.AttributeTotal(EquipSlotReinforceAttribute.Support)}, " +
            $"set level {character.EquipSlotReinforces.SetEffectLevel(EquipSlotReinforceAttribute.Offence)}, " +
            $"bundle {character.EquipSlotReinforces.BundleEffectLevel()}");
    }

    private void Set(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 3 ||
            !byte.TryParse(args[1], out var slotTypeId) ||
            !sbyte.TryParse(args[2], out var level))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] set <slot> <level> [exp]");
            return;
        }

        var exp = 0;
        if (args.Length > 3 && !int.TryParse(args[3], out exp))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] exp must be a number");
            return;
        }

        if (!character.EquipSlotReinforces.SetProgress(slotTypeId, level, exp, out var error))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] {error}");
            return;
        }

        var state = character.EquipSlotReinforces.StateOf(slotTypeId);
        messageOutput.SendMessage(
            $"[{CommandNames[0]}] slot {slotTypeId} now level {state.Level} exp {state.Exp}");
    }

    /// <summary>
    /// Re-rolls an effect the slot already obtained, which is what the window's rotate button does: the row it
    /// holds is dropped and another is rolled out of the same tier.
    /// </summary>
    private void Roll(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 2 || !byte.TryParse(args[1], out var slotTypeId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] roll <slot> [tier]");
            return;
        }

        var state = character.EquipSlotReinforces.StateOf(slotTypeId);
        if (state == null)
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] slot {slotTypeId} has no progress to roll");
            return;
        }

        var effects = character.EquipSlotReinforces.Effects
            .Where(effect => effect.SlotTypeId == slotTypeId)
            .ToList();
        if (effects.Count == 0)
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] slot {slotTypeId} has obtained no effect to roll");
            return;
        }

        var target = effects[^1];
        if (args.Length > 2 && uint.TryParse(args[2], out var tierId))
        {
            var match = effects.FirstOrDefault(effect => effect.LevelEffectId == tierId);
            if (match == null)
            {
                messageOutput.SendMessage($"[{CommandNames[0]}] slot {slotTypeId} has no effect from tier {tierId}");
                return;
            }

            target = match;
        }

        var rolled = character.EquipSlotReinforces.RerollTierEffect(slotTypeId, target.LevelEffectId);
        if (rolled == null)
        {
            messageOutput.SendMessage(
                $"[{CommandNames[0]}] slot {slotTypeId} tier {target.LevelEffectId} has nothing left to roll");
            return;
        }

        messageOutput.SendMessage(
            $"[{CommandNames[0]}] slot {slotTypeId} tier {target.LevelEffectId} rolled modifier {rolled.Id} " +
            $"({(UnitAttribute)rolled.UnitAttributeId} {rolled.Value})");
    }

    /// <summary>
    /// Switches an obtained effect on or off. This is the server half of the window's radio: picking a line
    /// applies it, "None" takes it off, and the character's stats follow either way.
    /// </summary>
    private void Apply(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 4 ||
            !byte.TryParse(args[1], out var slotTypeId) ||
            !uint.TryParse(args[2], out var tierId))
        {
            messageOutput.SendMessage($"[{CommandNames[0]}] apply <slot> <tier> <on|off>");
            return;
        }

        var applied = args[3].ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => (bool?)null
        };

        if (applied == null)
            messageOutput.SendMessage($"[{CommandNames[0]}] apply <slot> <tier> <on|off>");
        else if (!character.EquipSlotReinforces.SetEffectApplied(slotTypeId, tierId, applied.Value))
            messageOutput.SendMessage(
                $"[{CommandNames[0]}] slot {slotTypeId} has no effect from tier {tierId}");
        else
            messageOutput.SendMessage(
                $"[{CommandNames[0]}] slot {slotTypeId} tier {tierId} {(applied.Value ? "on" : "off")}");
    }
}
