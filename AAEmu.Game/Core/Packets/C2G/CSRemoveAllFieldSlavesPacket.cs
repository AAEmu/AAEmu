using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRemoveAllFieldSlavesPacket : GamePacket
{
    public CSRemoveAllFieldSlavesPacket() : base(CSOffsets.CSRemoveAllFieldSlavesPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16(); // target

        Logger.Debug("CSRemoveAllFieldSlaves, Tl: {0}", tlId);

        SlaveManager.Instance.UnbindSlave(Connection.ActiveChar, tlId, AttachUnitReason.SlaveUnbinding);
    }
}
