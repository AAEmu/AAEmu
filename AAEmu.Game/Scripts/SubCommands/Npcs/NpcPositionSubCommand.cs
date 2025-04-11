using System.Collections.Generic;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.SubCommands.Npcs;

public class NpcPositionSubCommand : SubCommandBase
{
    public NpcPositionSubCommand()
    {
        Title = "[Npc Position]";
        Description = "Change npc position and angle - All positions are optional use all or only the ones you want to change (Use yaw to rotate npc)";
        CallPrefix = $"{CommandManager.CommandPrefix}npc position||pos";
        AddParameter(new StringSubCommandParameter("target", "target", true, "target", "id"));
        AddParameter(new NumericSubCommandParameter<uint>("ObjId", "object id", false));
        AddParameter(new NumericSubCommandParameter<float>("x", "x=<new x>", false, "x"));
        AddParameter(new NumericSubCommandParameter<float>("y", "y=<new y>", false, "y"));
        AddParameter(new NumericSubCommandParameter<float>("z", "z=<new z>", false, "z"));
        AddParameter(new NumericSubCommandParameter<float>("roll", "roll=<new roll degrees>", false, "roll", 0, 360));
        AddParameter(new NumericSubCommandParameter<float>("pitch", "pitch=<new pitch degrees>", false, "pitch", 0, 360));
        AddParameter(new NumericSubCommandParameter<float>("yaw", "yaw=<new yaw degrees>", false, "yaw", 0, 360));
    }

    public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        Npc npc;
        if (parameters.TryGetValue("ObjId", out var npcObjId))
        {
            npc = WorldManager.Instance.GetNpc(npcObjId);
            if (npc is null)
            {
                SendColorMessage(messageOutput, Color.Red, $"Npc with objId {npcObjId} does not exist");
                return;
            }
        }
        else
        {
            var currentTarget = ((Character)character).CurrentTarget;
            if (currentTarget is null || !(currentTarget is Npc))
            {
                SendColorMessage(messageOutput, Color.Red, "You need to target a Npc first");
                return;
            }

            npc = (Npc)currentTarget;
        }

        var x = GetOptionalParameterValue(parameters, "x", npc.Transform.Local.Position.X);
        var y = GetOptionalParameterValue(parameters, "y", npc.Transform.Local.Position.Y);
        var z = GetOptionalParameterValue(parameters, "z", npc.Transform.Local.Position.Z);
        var yaw = GetOptionalParameterValue(parameters, "yaw", npc.Transform.Local.Rotation.Z.RadToDeg()).DegToRad();
        var pitch = GetOptionalParameterValue(parameters, "pitch", npc.Transform.Local.Rotation.Y.RadToDeg())
            .DegToRad();
        var roll = GetOptionalParameterValue(parameters, "roll", npc.Transform.Local.Rotation.X.RadToDeg()).DegToRad();

        SendMessage(messageOutput, $"Npc ObjId:{npc.ObjId} TemplateId:{npc.TemplateId}, x:{x}, y:{y}, z:{z}, roll:{roll.RadToDeg():0.#}°, pitch:{pitch.RadToDeg():0.#}°, yaw:{yaw.RadToDeg():0.#}°");

        npc.Transform.Local.SetPosition(x, y, z, roll, pitch, yaw);
        var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
        moveType.X = x;
        moveType.Y = y;
        moveType.Z = z;
        var npcRot = npc.Transform.Local.ToRollPitchYawSBytes();
        moveType.RotationX = npcRot.Item1;
        moveType.RotationY = npcRot.Item2;
        moveType.RotationZ = npcRot.Item3;

        moveType.Flags = MoveTypeFlags.Moving;
        moveType.DeltaMovement = new sbyte[3];
        moveType.DeltaMovement[0] = 0;
        moveType.DeltaMovement[1] = 0;
        moveType.DeltaMovement[2] = 0;
        moveType.Stance = GameStanceType.Relaxed; // 0; // combat=0, idle=1
        moveType.Alertness = MoveTypeAlertness.Idle; // idle=0, combat=2
        moveType.Time += 50; // has to change all the time for normal motion.
        character.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
    }
}
