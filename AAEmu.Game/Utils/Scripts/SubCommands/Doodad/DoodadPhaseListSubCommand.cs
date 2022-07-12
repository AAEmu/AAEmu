using System.Drawing;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadPhaseListSubCommand : SubCommandBase
    {
        public DoodadPhaseListSubCommand()
        {
            Title = "[Doodad Phase List]";
            Description = "List all the phases of a given doodad";
            CallPrefix = "/doodad phase list <ObjId>";
        }
        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            if (args.Length < 1)
            {
                SendMessage(character, "Missing parameters use: /doodad phase list <ObjId>");
                return;
            }

            if (!uint.TryParse(args[0], out var doodadObjId)) 
            {
                SendMessage(character, "Invalid ObjId, should be a number");
                return;
            }

            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            if (doodad is null)
            {
                SendColorMessage(character, Color.Red, "Doodad with objId {0} Does not exist |r", doodadObjId);
            }
            if (!(doodad is Doodad))
            {
                SendColorMessage(character, Color.Red, "Doodad with objId {0} is invalid (not a Doodad) |r", doodadObjId);
            }

            var availablePhases = string.Join(", ", DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId));

            SendMessage(character, "TemplateId {0}: ObjId{1}, Available phase ids (func groups): {2}", doodad.TemplateId, doodad.ObjId, availablePhases);
            _log.Warn($"{Title} Chain: TemplateId {doodad.TemplateId}, doodadObjId {doodad.ObjId},  Available phase ids (func groups): {availablePhases}");
        }
    }
}
