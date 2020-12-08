using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionDeclareHostilePacket : GamePacket
    {
        public CSFactionDeclareHostilePacket() : base(CSOffsets.CSFactionDeclareHostilePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Debug("FactionDeclareHostile, Id: {0}", id);
        }
    }
}
