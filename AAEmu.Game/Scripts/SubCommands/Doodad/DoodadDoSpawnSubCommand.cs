using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadDoSpawnSubCommand : SubCommandBase
    {
        public DoodadDoSpawnSubCommand()
        {
            Title = "[Doodad Do Spawn]";
            Description = "Expect a script command to spawn Doodad";
            CallPrefix = $"{CommandManager.CommandPrefix}doodad dospawn";
            AddParameter(new NumericSubCommandParameter<uint>("templateId", "template id", true));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint unitTemplateId = parameters["templateId"];
            if (!DoodadManager.Instance.Exist(unitTemplateId))
            {
                SendColorMessage(character, Color.Red, "Doodad templateId:{0} don't exist|r", unitTemplateId);
                return;
            }

            SendColorMessage(character, Color.Chartreuse, "Add the 'Dukling' item with the command '/item add self 19939 1'");
            SendColorMessage(character, Color.Chartreuse, "Then use it to place the substituted doodad in the right place");
            character.ExpectScriptCommandDoSpawn = unitTemplateId;
            return;
        }
    }
}
