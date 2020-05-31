using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpecialtyRatioPacket : GamePacket
    {
        public CSSpecialtyRatioPacket() : base(0x043, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            var ratio = SpecialtyManager.Instance.GetRatioForSpecialty(Connection.ActiveChar);
            Connection.ActiveChar.SendPacket(new SCSpecialtyRatioPacket(ratio));
        }
    }
}
