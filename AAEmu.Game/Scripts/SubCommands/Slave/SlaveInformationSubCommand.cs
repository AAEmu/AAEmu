using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class SlaveInformationSubCommand : SubCommandBase
    {
        public SlaveInformationSubCommand()
        {
            Title = "[Slave Information]";
            Description = "Get all slave information from a Slave (Targeted or by Id)";
            CallPrefix = $"{CommandManager.CommandPrefix}slave info";
            AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
            AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Models.Game.Units.Slave slave;
            if (parameters.TryGetValue("ObjId", out ParameterValue objId))
            {
                slave = (Models.Game.Units.Slave)WorldManager.Instance.GetGameObject(objId);
                if (slave is null)
                {
                    SendColorMessage(character, Color.Red, "Slave with objId {0} does not exist |r", objId);
                    return;
                }
            }
            else
            {
                var currentTarget = ((Character)character).CurrentTarget;
                if (currentTarget is null || !(currentTarget is Models.Game.Units.Slave))
                {
                    SendColorMessage(character, Color.Red, "You need to target a Slave first");
                    return;
                }
                slave = (Models.Game.Units.Slave) currentTarget;
            }

            var x = slave.Transform.Local.Position.X;
            var y = slave.Transform.Local.Position.Y;
            var z = slave.Transform.Local.Position.Z;
            var yaw = slave.Transform.Local.Rotation.Z.RadToDeg();
            var pitch = slave.Transform.Local.Rotation.Y.RadToDeg();
            var roll = slave.Transform.Local.Rotation.X.RadToDeg();

            //TODO: There is much more potential information to show on this command.
            SendMessage(character, $"Name:@NPC_NAME({slave.TemplateId}) ObjId:{slave.ObjId} TemplateId:{slave.TemplateId}, x:{x}, y:{y}, z:{z}, roll:{roll:0.#}°, pitch:{pitch:0.#}°, yaw:{yaw:0.#}°");
        }
    }
}
