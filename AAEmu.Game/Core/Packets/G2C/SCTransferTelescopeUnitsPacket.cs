using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTransferTelescopeUnitsPacket : GamePacket
    {
        private readonly bool _last;
        private readonly Transfer[] _transfers;

        public SCTransferTelescopeUnitsPacket(bool last, Transfer[] transfers) : base(SCOffsets.SCTransferTelescopeUnitsPacket, 1)
        {
            _last = last;
            _transfers = transfers;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_last);
            stream.Write((byte)_transfers.Length);
            foreach (var transfer in _transfers)
            {
                transfer.WriteTelescopeUnit(stream);
            }

            return stream;
        }
    }
}
