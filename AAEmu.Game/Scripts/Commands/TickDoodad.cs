using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands
{
    public class TickDoodad : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            CommandManager.Instance.Register("tickdoodad", this);
        }

        public string GetCommandLineHelp()
        {
            return "<objId>";
        }

        public string GetCommandHelpText()
        {
            return "Moves a doodad onto it's next Phase using <objId> inside a <radius> range.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length < 1)
            {
                character.SendMessage("[tickdoodad] " + CommandManager.CommandPrefix + "tickdoodad " + GetCommandLineHelp());
                return;
            }

            uint unitId;
            float radius = 30f;
            if (!uint.TryParse(args[0], out unitId))
            {
                character.SendMessage("|cFFFF0000[tickdoodad] Parse error unitId|r");
                return;
            }
            var tickedCount = 0;
            // Use radius
            var myDoodads = WorldManager.Instance.GetAround<Doodad>(character, radius);
            foreach (var doodad in myDoodads)
            {
                if (doodad.TemplateId == unitId)
                {
                    if (doodad.FuncTask != null)
                    {
                        doodad.FuncTask.Cancel();
                        doodad.FuncTask.Execute();
                        tickedCount++;
                    }
                }
            }
            character.SendMessage("[tickdoodad] phased {0} Doodad(s) with TemplateID {1} - @DOODAD_NAME({1})", tickedCount, unitId);
        }
    }
}
