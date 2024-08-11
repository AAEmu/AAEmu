using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Appellation : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "title", "addtitle", "add_title", "appellation" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "<titleId>";
    }

    public string GetCommandHelpText()
    {
        return "Adds title using <titleId>";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        if (uint.TryParse(args[0], out var id))
        {
            if (CharacterManager.Instance.GetAppellationsTemplate(id) == null)
            {
                CommandManager.SendErrorText(this, messageOutput, $"<titleId> {id} does not exist in the database");
            }
            else
            {
                character.Appellations.Add(id);
            }
        }
        else
        {
            CommandManager.SendErrorText(this, messageOutput, "Error parsing <titleId> !");
        }
    }
}
