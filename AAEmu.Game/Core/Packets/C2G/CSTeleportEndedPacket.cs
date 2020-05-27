using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTeleportEndedPacket : GamePacket
    {
        public CSTeleportEndedPacket() : base(0x0b4, 1) // 0x0b0
        {
        }

        public override void Read(PacketStream stream)
        {
            var x = Helpers.ConvertLongX(stream.ReadInt64());
            var y = Helpers.ConvertLongY(stream.ReadInt64());
            var z = stream.ReadSingle();
            var ori = stream.ReadBytes(16); // TODO example: 00000000 00000000 00000000 0000803F

            DbLoggerCategory.Database.Connection.ActiveChar.DisabledSetPosition = false;
            _log.Warn("TeleportEnded, X: {0}, Y: {1}, Z: {2}", x, y, z);

            WorldManager.Instance.ResendVisibleObjectsToCharacter(DbLoggerCategory.Database.Connection.ActiveChar);
        }
    }
}
