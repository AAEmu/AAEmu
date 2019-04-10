using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyChangeOwnerPacket : GamePacket
    {
        public CSFamilyChangeOwnerPacket() : base(0x01f, 1) //TODO : 1.0 opcode: 0x01e
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
