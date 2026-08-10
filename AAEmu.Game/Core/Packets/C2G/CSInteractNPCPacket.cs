using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// <c>bool isTargetChanged</c>.
/// </remarks>
public class CSInteractNPCPacket() : GamePacket(CSOffsets.CSInteractNPCPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var isTargetChanged = stream.ReadBoolean();

        Logger.Debug("InteractNPC, BcId: {0}, TargetChanged: {1}", objId, isTargetChanged);

        var character = Connection.ActiveChar;
        if (character == null || objId == 0 || character.ParentWorld.GetUnit(objId) is not Npc npc)
        {
            Logger.Warn(
                "Rejected NPC interaction target {0} from {1} ({2})",
                objId, character?.Name ?? "<disconnected>", character?.ObjId ?? 0);
            return;
        }

        character.CurrentInteractionObject = npc;

        if (isTargetChanged)
            character.CurrentTarget = npc;

        // A zero-entry table is the native representation of no known aggro for this NPC.
        Connection.SendPacket(new SCAiAggroPacket(objId));

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayInteractNpcToZone?.Invoke(character.ObjId, objId, false);
    }
}
