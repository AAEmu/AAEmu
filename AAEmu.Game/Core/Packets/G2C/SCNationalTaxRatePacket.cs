using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNationalTaxRatePacket : GamePacket
    {
        private readonly ushort _id;
        private readonly int _taxRate;
        private readonly int _prevTaxRate;
        private readonly DateTime _changedTime;

        public SCNationalTaxRatePacket(ushort id, int taxRate, int prevTaxRate, DateTime changedTime) : base(SCOffsets.SCNationalTaxRatePacket, 5)
        {
            _id = id;
            _taxRate = taxRate;
            _prevTaxRate = prevTaxRate;
            _changedTime = changedTime;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_taxRate);
            stream.Write(_prevTaxRate);
            stream.Write(_changedTime);
            return stream;
        }
    }
}
