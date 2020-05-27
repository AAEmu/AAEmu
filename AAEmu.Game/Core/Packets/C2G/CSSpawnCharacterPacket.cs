using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Observers;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSpawnCharacterPacket : GamePacket
    {
        public CSSpawnCharacterPacket() : base(0x026, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            DbLoggerCategory.Database.Connection.State = GameState.World;

            DbLoggerCategory.Database.Connection.ActiveChar.VisualOptions = new CharacterVisualOptions();
            DbLoggerCategory.Database.Connection.ActiveChar.VisualOptions.Read(stream);

            DbLoggerCategory.Database.Connection.SendPacket(new SCUnitStatePacket(DbLoggerCategory.Database.Connection.ActiveChar));

            DbLoggerCategory.Database.Connection.ActiveChar.PushSubscriber(
                TimeManager.Instance.Subscribe(DbLoggerCategory.Database.Connection, new TimeOfDayObserver(DbLoggerCategory.Database.Connection.ActiveChar))
            );

            _log.Info("CSSpawnCharacterPacket");
        }
    }
}
