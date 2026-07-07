using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Scripts.SubCommands.Crimes;
using AAEmu.Game.Scripts.SubCommands.Doodads;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands;

public class CrimeCmd : SubCommandBase, ICommand
{
    public string[] CommandNames { get; set; } = ["doodad"];

    public CrimeCmd()
    {
        Title = "[Crime]";
        Description = "Root command to crime related tasks";
        CallPrefix = $"{CommandManager.CommandPrefix}{CommandNames[0]}";

        Register(new CrimeCreateSubCommand(), "create");
        Register(new CrimeAddPointSubCommand(), "points");
        Register(new CrimeJuryInviteSubCommand(), ["jury_invite", "ji"]);
        Register(new CrimeAskGuiltySubCommand(), ["ask_guilty", "ag"]);
        Register(new CrimeCourtSubCommand(), "court");
        Register(new CrimeSetTrialStateSubCommand(), ["trial_state", "ts"]);
        Register(new CrimeFakeTrialSubCommand(), "fake");
    }

    public void OnLoad()
    {
        CommandManager.Instance.Register("crime", this);
    }

    public CrimeCmd(Dictionary<ICommandV2, string[]> subcommands) : base(subcommands)
    {
    }

    public string GetCommandLineHelp()
    {
        return $"<{string.Join("||", SupportedCommands)}>";
    }

    public string GetCommandHelpText()
    {
        return CallPrefix;
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        throw new InvalidOperationException($"A {nameof(ICommandV2)} implementation should not be used as ICommand interface");
    }
}
