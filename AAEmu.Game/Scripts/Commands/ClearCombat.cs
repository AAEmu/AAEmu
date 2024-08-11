using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class ClearCombat : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "clearcombat", "clear_combat", "cc" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Force sends a clear combat packet. Does not actually clear the combat flag on the server!";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        character.SendPacket(new SCCombatClearedPacket(character.ObjId));
    }
}
