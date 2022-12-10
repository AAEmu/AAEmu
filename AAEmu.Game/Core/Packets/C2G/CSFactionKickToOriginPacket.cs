using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionKickToOriginPacket : GamePacket
    {
        public CSFactionKickToOriginPacket() : base(CSOffsets.CSFactionKickToOriginPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var name = stream.ReadString();
            
            _log.Debug("FactionKickToOrigin, Name: {0}", name);
        }
    }
}
