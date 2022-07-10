using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCofferContentsUpdatePacket : GamePacket
    {
        public const byte MaxSlotsToSend = 30;
        private readonly DoodadCoffer _cofferDoodad;
        private readonly byte _firstSlot;
        private readonly byte _slotCount;

        public SCCofferContentsUpdatePacket(DoodadCoffer cofferDoodad, byte firstSlot) : base(SCOffsets.SCCofferContentsUpdatePacket, 1)
        {
            _cofferDoodad = cofferDoodad;
            _firstSlot = firstSlot;
            var lastSlot = _firstSlot + MaxSlotsToSend ;
            if (lastSlot >= _cofferDoodad.Capacity)
                lastSlot = _cofferDoodad.Capacity ;
            var slotCount = lastSlot - _firstSlot;
            if (slotCount >= MaxSlotsToSend)
                slotCount = MaxSlotsToSend;
            _slotCount = (byte)slotCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            if (_cofferDoodad?.ItemContainer == null)
                _log.Warn($"No ItemContainer assigned to Coffer, objId: {_cofferDoodad?.ObjId} dbId: {_cofferDoodad?.DbId}");
            
            stream.WriteBc(_cofferDoodad?.ObjId ?? 0);
            stream.Write(_cofferDoodad?.GetItemContainerId() ?? 0);
            stream.Write(_slotCount);
            for (byte i = 0; i < _slotCount; i++)
            {
                var slot = (byte)(_firstSlot + i);
                stream.Write(slot);
                var item = _cofferDoodad?.ItemContainer?.GetItemBySlot(slot);
                if (item == null)
                    stream.Write(0u); // uint
                else
                    stream.Write(item);
            }

            return stream;
        }
    }
}
