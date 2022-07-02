using System.Drawing;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class DoodadPhaseSubCommand : SubCommandBase
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public DoodadPhaseSubCommand()
        {
            Prefix = "[Doodad Phase]";
            Description = "Change the phase of a given Doodad";
            CallExample = "/doodad setphase|phase <ObjId> <PhaseId>";
        }
        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            if (args.Length < 2)
            {
                SendMessage(character, "Missing parameters use: /doodad setphase|phase <ObjId> <PhaseId>");
                return;
            }

            if (!uint.TryParse(args[0], out var doodadObjId)) 
            {
                SendMessage(character, "Invalid ObjId, should be a number");
                return;
            }

            if (!int.TryParse(args[1], out var phase))
            {
                SendMessage(character, "Invalid PhaseId, should be a number");
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

            var listIds = DoodadManager.Instance.GetDoodadFuncGroupsId(doodad.TemplateId);
            SendMessage(character, "SetPhase {0}", phase);
            SendMessage(character, "TemplateId {0}: ObjId{1}, SetPhase {2}, Phases({3})", doodad.TemplateId, doodad.ObjId, phase, string.Join(", ", listIds));
            _log.Warn($"{Prefix} Chain: TemplateId {doodad.TemplateId}, doodadObjId {doodad.ObjId}, SetPhase {phase}, Phases({string.Join(", ", listIds)}");
            doodad.DoPhaseFuncs((Unit)character, phase);
        }
    }
}
