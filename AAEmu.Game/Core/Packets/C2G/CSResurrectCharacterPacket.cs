using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResurrectCharacterPacket : GamePacket
    {
        public CSResurrectCharacterPacket() : base(0x04e, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var inPlace = stream.ReadBoolean();

            _log.Debug("ResurrectCharacter, InPlace: {0}", inPlace);

            DbLoggerCategory.Database.Connection.ActiveChar.Hp = (int)(DbLoggerCategory.Database.Connection.ActiveChar.MaxHp * 0.1);
            DbLoggerCategory.Database.Connection.ActiveChar.Mp = (int)(DbLoggerCategory.Database.Connection.ActiveChar.MaxMp * 0.1);

            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                new SCCharacterResurrectedPacket(
                    DbLoggerCategory.Database.Connection.ActiveChar.ObjId,
                    DbLoggerCategory.Database.Connection.ActiveChar.Position.X,
                    DbLoggerCategory.Database.Connection.ActiveChar.Position.Y,
                    DbLoggerCategory.Database.Connection.ActiveChar.Position.Z,
                    0
                ),
                true
            );

            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(
                new SCUnitPointsPacket(
                    DbLoggerCategory.Database.Connection.ActiveChar.ObjId,
                    DbLoggerCategory.Database.Connection.ActiveChar.Hp,
                    DbLoggerCategory.Database.Connection.ActiveChar.Mp
                ),
                true
            );
            DbLoggerCategory.Database.Connection.ActiveChar.StartRegen();
        }
    }
}
