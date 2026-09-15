using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSFamilyChangeOwnerPacket() : GamePacket(CSOffsets.CSFamilyChangeOwnerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var wireId = stream.ReadUInt64();
        if (wireId > uint.MaxValue)
        {
            Logger.Warn("Ignoring FamilyChangeOwner with unsupported character id: {0}", wireId);
            return;
        }

        var id = (uint)wireId;

        FamilyManager.Instance.ChangeOwner(Connection.ActiveChar, id);

        Logger.Debug("FamilyChangeOwner, Id: {0}", id);
    }
}
