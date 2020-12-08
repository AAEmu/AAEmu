using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyChangeOwnerPacket : GamePacket
    {
        public CSFamilyChangeOwnerPacket() : base(CSOffsets.CSFamilyChangeOwnerPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            FamilyManager.Instance.ChangeOwner(Connection.ActiveChar, id);
            
            _log.Debug("FamilyChangeOwner, Id: {0}", id);
        }
    }
}
