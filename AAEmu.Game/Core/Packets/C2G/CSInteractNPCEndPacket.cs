using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInteractNPCEndPacket() : GamePacket(CSOffsets.CSInteractNPCEndPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        Logger.Debug("InteractNPCEnd, BcId: {0}", objId);

        Connection.ActiveChar.CurrentInteractionObject = null;

        if (WorldIntegration.ZoneAuthority && Connection.ActiveChar != null)
            WorldIntegration.RelayInteractNpcToZone?.Invoke(Connection.ActiveChar.ObjId, objId, true);
    }
}
