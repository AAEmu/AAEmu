using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// bc object-id primitive (slot 0x1a0, len 3).
/// </remarks>
public class CSInteractGimmickPacket() : GamePacket(CSOffsets.CSInteractGimmickPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var gimmickObjId = stream.ReadBc();
        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var gimmickManager = character.ParentWorld?.GimmickManager;
        if (gimmickManager?.OwnsGimmick(gimmickObjId) == true)
            gimmickManager.Interact(character, gimmickObjId);
        else
            WorldIntegration.TryInteractZoneGimmick?.Invoke(character, gimmickObjId);
    }
}
