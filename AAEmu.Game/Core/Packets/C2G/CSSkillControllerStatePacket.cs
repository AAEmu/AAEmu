using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.SkillControllers;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSkillControllerStatePacket() : GamePacket(CSOffsets.CSSkillControllerStatePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var scType = stream.ReadByte();
        float? len = null;
        bool? teared = null;
        bool? cutouted = null;
        if (scType == 0)
        {
            len = stream.ReadSingle();
            teared = stream.ReadBoolean();
            cutouted = stream.ReadBoolean();
        }

        SkillControllerPacketDebug.LogCsSkillControllerState(objId, scType, len, teared, cutouted);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (scType == 0)
        {
            if (!len.HasValue || !teared.HasValue || !cutouted.HasValue)
                return;

            if (!ShipHarpoonRopeController.TryApplySkillControllerState(
                    character,
                    objId,
                    len.Value,
                    cutouted.Value,
                    !WorldIntegration.ZoneAuthority,
                    out var appliedLength,
                    out var appliedTeared,
                    out var appliedCutouted))
            {
                Logger.Warn(
                    "Rejected skill-controller state type {0} for object {1} from {2} ({3})",
                    scType, objId, character.Name, character.ObjId);
                return;
            }

            WorldIntegration.RelaySkillControllerStateToZone?.Invoke(
                objId, scType, appliedLength, appliedTeared, appliedCutouted);
            return;
        }

        if (!SkillControllerAuthority.CanControl(character, objId))
        {
            Logger.Warn(
                "Rejected skill-controller state type {0} for object {1} from {2} ({3})",
                scType, objId, character.Name, character.ObjId);
            return;
        }

        WorldIntegration.RelaySkillControllerStateToZone?.Invoke(objId, scType, 0f, false, false);
    }
}
