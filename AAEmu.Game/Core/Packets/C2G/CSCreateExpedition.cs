using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateExpedition : GamePacket
    {
        public CSCreateExpedition() : base(0x04, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var expeditionName = stream.ReadString();

            _log.Debug("CSCreateExpedition : ExpeditionName = {0}", expeditionName);
        }
    }
}