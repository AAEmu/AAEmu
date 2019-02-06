using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFamilyChangeOwnerPacket : GamePacket
    {
        public CSFamilyChangeOwnerPacket() : base(0x01e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Debug("FamilyChangeOwner, Id: {0}", id);
        }
    }
}
