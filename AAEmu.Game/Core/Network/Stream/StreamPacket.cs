using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Stream
{
    public abstract class StreamPacket : PacketBase<StreamConnection>
    {
        protected StreamPacket(ushort typeId) : base(typeId)
        {
        }

        public override PacketStream Encode()
        {
            var ps = new PacketStream();
            try
            {
                ps.Write(new PacketStream().Write(TypeId).Write(this));
            }
            catch (Exception ex)
            {
                _log.Fatal(ex);
                throw;
            }
            
            _log.Debug("StreamPacket: S->C\n{0}", ps);

            return ps;
        }

        public override PacketBase<StreamConnection> Decode(PacketStream ps)
        {
            _log.Debug("StreamPacket: C->S\n{0}", ps);
            
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