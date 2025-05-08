using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDiscardSlavePacket() : GamePacket(CSOffsets.CSDiscardSlavePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16();

        Logger.Debug($"DiscardSlave, Tl: {tlId}");
        Connection.ActiveChar.ParentWorld.SlaveManager.UnbindSlave(Connection.ActiveChar, tlId, AttachUnitReason.SlaveBinding);
    }
}
