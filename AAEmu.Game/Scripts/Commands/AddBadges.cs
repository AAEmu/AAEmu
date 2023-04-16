using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddBadges : ICommand
    {
        public void OnLoad()
        {
            string[] name = { "addbadges", "add_vp", "add_vb", "vp" };
            CommandManager.Instance.Register(name, this);
        }

        public string GetCommandLineHelp()
        {
            return "(target) <vp>";
        }

        public string GetCommandHelpText()
        {
            return "Adds vocation points (to target player)";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[VocationPoint] " + CommandManager.CommandPrefix + "add_vp (target) <VocationPoint>");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            var vptoadd = 0;
            if (int.TryParse(args[firstarg + 0], out int parsevp))
            {
                vptoadd = parsevp;
            }

            if (vptoadd > 0)
                targetPlayer.ChangeGamePoints((GamePointKind)1, vptoadd);
        }
    }
}
