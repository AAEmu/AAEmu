using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;
using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// GM: grants Black Pearl (Growling Yawl) + mythic sails/propellant/figurehead.
/// Prefer plain <c>/item add</c> when only one template is needed.
/// </summary>
public class GiveBlackPearlKit : ICommand
{
    public string[] CommandNames { get; set; } = ["giveblackpearl", "gbp"];

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly (uint id, int count, byte grade)[] Kit =
    [
        (13711, 1, 0),   // Growling Yawl / Black Pearl summon scroll
        (43729, 2, 11),  // Growling Zephyr Square Foresail (Mythic)
        (43736, 1, 11),  // Growling Zephyr Square Mainsail (Mythic)
        (43001, 1, 11),  // Sea Serpent Propellant (Mythic)
        (43730, 1, 11),  // Tsunami Figurehead (Mythic)
    ];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp() => "(optional player name)";

    public string GetCommandHelpText() =>
        "Gives Black Pearl + mythic Growling Zephyr foresails x2, mainsail, Sea Serpent Propellant, Tsunami Figurehead.";

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        var name = args.Length > 0 ? args[0] : character.Name;
        var target = WorldManager.Instance.GetCharacter(name);
        if (target == null)
        {
            character.SendMessage(ChatType.System, $"Player {name} not online.", Color.Red);
            return;
        }

        var n = Grant(target);
        character.SendMessage(ChatType.System, $"Granted {n} stacks to {target.Name}.");
    }

    private static int Grant(Character target)
    {
        var added = 0;
        foreach (var (id, count, grade) in Kit)
        {
            if (!target.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, id, count, grade))
            {
                Logger.Warn("GiveBlackPearlKit: failed item {0} x{1} grade {2} for {3}", id, count, grade, target.Name);
                target.SendMessage(ChatType.System, $"Could not add item {id} (bag full?)", Color.Red);
                continue;
            }

            added++;
        }

        target.SendMessage(ChatType.System,
            "|cFF00FF00[GM] Black Pearl kit: ship scroll, 2× Mythic Foresails, Mythic Mainsail, Mythic Sea Serpent Propellant, Mythic Tsunami Figurehead.|r");
        return added;
    }
}
