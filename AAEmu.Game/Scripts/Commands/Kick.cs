using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Kick : ICommand
{
    public string[] CommandNames { get; set; } = ["kick_player", "kick"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "(character name || id) (reason) (msg)";
    }

    public string GetCommandHelpText()
    {
        return "Kicks target";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length < 3)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        var targetChar = uint.TryParse(args[0], out var characterId)
            ? WorldManager.Instance.GetCharacterById(characterId)
            : WorldManager.Instance.GetCharacter(args[0]);

        if (targetChar == null)
        {
            CommandManager.SendNormalText(this, messageOutput, $"Target not found");
            return;
        }

        var reason = (KickedReason)byte.Parse(args[1]);
        var msg = "";
        for (var x = 2; x < args.Length; x++)
        {
            msg += args[x] + " ";
        }

        targetChar.SendPacket(new SCKickedPacket(reason, msg));
    }
}
