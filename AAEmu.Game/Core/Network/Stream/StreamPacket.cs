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
            
            _log.Debug("StreamPacket: S->C {1}\n{0}", ps, this.ToString().Substring(23));

            return ps;
        }

        public override PacketBase<StreamConnection> Decode(PacketStream ps)
        {
            _log.Debug("StreamPacket: C->S {1}\n{0}", ps, this.ToString().Substring(23));
            
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
