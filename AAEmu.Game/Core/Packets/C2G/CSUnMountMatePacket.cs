using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSUnMountMatePacket() : GamePacket(CSOffsets.CSUnMountMatePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16();
        var ap = (AttachPointKind)stream.ReadByte();
        var reason = (AttachUnitReason)stream.ReadByte();

        //Logger.Warn("UnMountMate, TlId: {0}, Ap: {1}, Reason: {2}", tlId, ap, reason);
        Connection.ActiveChar.ParentWorld.MateManager.UnMountMate(Connection.ActiveChar, tlId, ap, reason);
    }
}
