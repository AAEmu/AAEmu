using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;

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

            if ((TypeId == TCOffsets.TCDoodadIdsPacket) || ((TypeId == TCOffsets.TCDoodadStreamPacket)))
                _log.Trace("StreamPacket: S->C type {0:X3} {1}", TypeId, this.ToString().Substring(23));
            else
                _log.Debug("StreamPacket: S->C {1}\n{0}", ps, this.ToString().Substring(23));

            return ps;
        }

        public override PacketBase<StreamConnection> Decode(PacketStream ps)
        {
            //_log.Trace("StreamPacket: C->S type {0:X3} {1}", TypeId, this.ToString().Substring(23));
            _log.Debug("StreamPacket: C->S type {0:X3} {2}\n{1}", TypeId, ps, this.ToString().Substring(23));

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
