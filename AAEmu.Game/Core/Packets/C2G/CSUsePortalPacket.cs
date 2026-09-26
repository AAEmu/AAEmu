using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUsePortalPacket() : GamePacket(CSOffsets.CSUsePortalPacket, 1)
{
    // 0x0da

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var onlyMyPortal = stream.ReadBoolean();

        Logger.Debug("UsePortal, ObjId: {0}, OnlyMyPortal: {1}", objId, onlyMyPortal);

        // onlyMyPortal is the client asking the server to restrict the use to the owner's own
        // portal. It was read and logged but never enforced, so any player could use another
        // character's stale pair.
        PortalManager.Instance.UsePortal(Connection.ActiveChar, objId, onlyMyPortal);
    }
}
