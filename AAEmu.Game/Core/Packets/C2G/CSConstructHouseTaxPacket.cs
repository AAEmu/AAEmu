using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConstructHouseTaxPacket : GamePacket
    {
        public CSConstructHouseTaxPacket() : base(0x054, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var designX = Helpers.ConvertLongX(stream.ReadInt64());
            var designY = Helpers.ConvertLongY(stream.ReadInt64());
            var designZ = stream.ReadSingle();
            var id = stream.ReadUInt32(); // type(id)
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            
            _log.Debug("ConstructHouseTax");
        }
    }
}
