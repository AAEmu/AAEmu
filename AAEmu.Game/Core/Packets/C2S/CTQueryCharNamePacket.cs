using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Core.Packets.S2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTQueryCharNamePacket : StreamPacket
    {
        public CTQueryCharNamePacket() : base(0x0a)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            var name = NameManager.Instance.GetCharacterName(id);
            if (name != null)
                DbLoggerCategory.Database.Connection.SendPacket(new TCCharNameQueriedPacket(id, name));

            _log.Debug("QueryCharName, Id: {0}, Name: {1}", id, name);
        }
    }
}
