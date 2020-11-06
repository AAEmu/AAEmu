using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Scripts.Commands
{
    public class Position : ICommand
    {
        public void OnLoad()
        {
            string[] names = { "position", "pos" };
            CommandManager.Instance.Register(names, this);
        }

        public string GetCommandLineHelp()
        {
            return "(player)";
        }

        public string GetCommandHelpText()
        {
            return "Displays information about the position of you, or your target if a target is selected or provided as a argument.";
        }

        public void Execute(Character character, string[] args)
        {
            if (character.CurrentTarget != null && character.CurrentTarget != character)
            {
                var pos = character.CurrentTarget.Position;

                if (character.CurrentTarget is Npc npc)
                    character.SendMessage("[Position] Id: {0}, ObjId: {1}, TemplateId: {2} X: |cFFFFFFFF{3}|r  Y: |cFFFFFFFF{4}|r  Z: |cFFFFFFFF{5}|r", npc.Spawner.Id, character.CurrentTarget.ObjId, npc.TemplateId, pos.X, pos.Y, pos.Z);
            }
            else
            {
                Character targetPlayer = character;
                if (args.Length > 0)
                    targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);


                var pos = targetPlayer.Position;

                var zonename = "???";
                var zone = ZoneManager.Instance.GetZoneByKey(pos.ZoneId);
                if (zone != null)
                    zonename = "@ZONE_NAME(" + zone.Id.ToString() + ")";

                character.SendMessage("[Position] |cFFFFFFFF{0}|r X: |cFFFFFFFF{1:F1}|r  Y: |cFFFFFFFF{2:F1}|r  Z: |cFFFFFFFF{3:F1}|r  RotZ: |cFFFFFFFF{4:F0}|r  ZoneId: |cFFFFFFFF{5}|r {6}",
                    targetPlayer.Name, pos.X, pos.Y, pos.Z, pos.RotationZ, pos.ZoneId, zonename);
            }
        }
    }
}
