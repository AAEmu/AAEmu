using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSConstructHouseTaxPacket : GamePacket
    {
        public CSConstructHouseTaxPacket() : base(0x056, 1) // TODO 1.0 opcode: 0x054
        {
        }

        public override void Read(PacketStream stream)
        {
            var designId = stream.ReadUInt32(); // type(id)
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            
            _log.Debug("ConstructHouseTax");

            HousingManager.Instance.GetTax(Connection.ActiveChar, designId);

        }
        
    }
}
