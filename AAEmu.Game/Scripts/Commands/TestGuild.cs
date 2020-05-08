using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;

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
                character.SendMessage("[TestGuild] " + CommandManager.CommandPrefix + "testguild " + GetCommandLineHelp());
                return;
            }

            var guildName = string.Empty;
            foreach(var a in args)
            {
                if (guildName != string.Empty)
                    guildName += " ";
                guildName += a;
            }

            ExpeditionManager.Instance.CreateExpedition(guildName, character.Connection);
        }
    }
}
