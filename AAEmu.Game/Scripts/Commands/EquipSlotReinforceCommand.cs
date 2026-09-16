using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
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
        return "<list|set> [slot] [level] [exp]";
    }

    public string GetCommandHelpText()
    {
        return "reinforce list - every reinforcement slot and this character's progress on it.\n" +
               "reinforce set <slot> <level> [exp] - set a slot's level and bar.";
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
                $"slot {slotTypeId} ({attribute}) level {state?.Level ?? 0}/{ladder[^1].Level} exp {state?.Exp ?? 0} " +
                $"effect {state?.LevelEffectIndex ?? -1}");
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
}
