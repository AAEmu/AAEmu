using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;
using AAEmu.Game.Models.Game.Skills.SkillControllers;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateSkillControllerPacket() : GamePacket(CSOffsets.CSCreateSkillControllerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        var fallDamageImmune = stream.ReadBoolean();

        SkillControllerPacketDebug.LogCsCreateSkillController(objId, scType, fallDamageImmune);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (!SkillControllerAuthority.CanControl(character, objId))
        {
            Logger.Warn(
                "Rejected skill-controller create type {0} for object {1} from {2} ({3})",
                scType, objId, character.Name, character.ObjId);
            return;
        }

        WorldIntegration.RelayCreateSkillControllerToZone?.Invoke(objId, scType, fallDamageImmune);
    }
}
