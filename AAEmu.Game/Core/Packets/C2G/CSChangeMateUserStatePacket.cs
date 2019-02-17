using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateUserStatePacket : GamePacket
    {
        public CSChangeMateUserStatePacket() : base(0x0aa, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            var userState = stream.ReadByte();
            
            _log.Warn("ChangeMateUserState, TlId: {0}, UserState: {1}", tl, userState);
        }
    }
}
