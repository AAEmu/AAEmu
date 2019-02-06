using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveTutorialPacket : GamePacket
    {
        public CSSaveTutorialPacket() : base(0x0f2, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();

            _log.Debug("SaveTutorial, Id: {0}", id);
        }
    }
}
