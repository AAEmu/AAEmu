using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Scripts.Commands
{
    public class TestGuild : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "testguild", "test_guild" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "<GuildName>";
        }

        public string GetCommandHelpText()
        {
            return "Manually creates a guild from your current party. Normal rules still apply.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage($"[TestGuild] {CommandManager.CommandPrefix}testguild {GetCommandLineHelp()}");
                return;
            }

            ExpeditionManager.Instance.CreateExpedition(string.Join(' ', args), character.Connection);
        }
    }
}
