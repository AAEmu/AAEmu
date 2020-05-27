using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeMateUserStatePacket : GamePacket
    {
        public CSChangeMateUserStatePacket() : base(0x0aa, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();
            var userState = stream.ReadByte();

            //_log.Warn("ChangeMateUserState, TlId: {0}, UserState: {1}", tlId, userState);
            MateManager.Instance.ChangeStateMate(DbLoggerCategory.Database.Connection, tlId, userState);
        }
    }
}
