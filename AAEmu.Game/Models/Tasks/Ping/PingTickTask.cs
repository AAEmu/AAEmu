using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Models.Tasks.Ping
{
    public class PingTickTask : Task
    {
        private GameConnection _connection;
        private long _tm;
        private long _when;
        private uint _local;

        public PingTickTask(GameConnection connection, long tm, long when, uint local)
        {
            _connection = connection;
            _tm = tm;
            _when = when;
            _local = local;
        }

        public override void Execute()
        {
            _connection.SendPacket(new PongPacket(_tm, _when, _local));
        }
    }
}
