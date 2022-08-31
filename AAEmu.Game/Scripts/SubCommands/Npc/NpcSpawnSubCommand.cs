using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;


namespace AAEmu.Game.Scripts.Commands
{
    public class NpcSpawnSubCommand : SubCommandBase
    {
        public NpcSpawnSubCommand()
        {
            Title = "[Npc Spawn]";
            Description = "Spawn one npc in front of the player facing player (default) or a optional direction in degrees";
            CallPrefix = $"{CommandManager.CommandPrefix}npc spawn";
            AddParameter(new NumericSubCommandParameter<uint>("NpcTemplateId", "Npc template Id", true));
            AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<facing degrees>", false, "yaw", 0, 360));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            uint npcTemplateId = parameters["NpcTemplateId"];

            if (!NpcManager.Instance.Exist(npcTemplateId))
            {
                SendColorMessage(character, Color.Red, $"NPC template {npcTemplateId} doesn't exist|r");
                return;
            }
            var selfCharacter = (Character)character;
            
            var npcSpawner = new NpcSpawner();
            npcSpawner.Id = 0;
            npcSpawner.UnitId = npcTemplateId;
            var charPos = selfCharacter.Transform.CloneDetached();
            charPos.Local.AddDistanceToFront(3f);
            npcSpawner.Position = charPos.CloneAsSpawnPosition();

            var angle = GetOptionalParameterValue(parameters, "yaw", (float)MathUtil.CalculateAngleFrom(charPos, selfCharacter.Transform)).DegToRad();


            if (!parameters.ContainsKey("yaw"))
            {
                SendMessage(character, $"NPC {npcTemplateId} facing you using angle {angle.RadToDeg():0.#}°");
            }
            else
            {
                SendMessage(character, $"NPC {npcTemplateId} using angle {angle.RadToDeg():0.#}°");
            }

            npcSpawner.Position.Yaw = angle;
            npcSpawner.Position.Pitch = 0;
            npcSpawner.Position.Roll = 0;

            SpawnManager.Instance.AddNpcSpawner(npcSpawner);

            npcSpawner.SpawnAll();
        }
    }
}
