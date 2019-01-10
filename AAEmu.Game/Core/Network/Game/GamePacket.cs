using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Game
{
    public abstract class GamePacket : PacketBase<GameConnection>
    {
        public byte Level { get; set; }

        protected GamePacket(ushort typeId, byte level) : base(typeId)
        {
            Level = level;
        }

        public override PacketStream Encode()
        {
            var ps = new PacketStream();
            try
            {
                ps.Write(new PacketStream().Write((byte) 0xdd).Write(Level).Write(TypeId).Write(this));
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }
            //не выводим Pong и SCUnitMovementsPacket
            if (!(TypeId == 0x013 && Level == 2) && !(TypeId == 0x066 && Level == 1))
                _log.Debug("GamePacket: S->C\n{0}", ps);

            return ps;
        }

        public override PacketBase<GameConnection> Decode(PacketStream ps)
        {
            //не выводим Ping и CSMoveUnitPacket
            if (!(TypeId == 0x012 && Level == 2) && !(TypeId == 0x088 && Level == 1))
                _log.Debug("GamePacket: C->S\n{0}", ps);

            try
            {
                Read(ps);
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }

            return this;
        }
    }
}