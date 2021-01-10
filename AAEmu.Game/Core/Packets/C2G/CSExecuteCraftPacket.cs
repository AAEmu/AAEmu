using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExecuteCraftPacket : GamePacket
    {
        public CSExecuteCraftPacket() : base(CSOffsets.CSExecuteCraftPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var craftId = stream.ReadUInt32();
            var objId = stream.ReadBc();
            var count = stream.ReadInt32();

            _log.Debug("CSExecuteCraft, craftId : {0} , objId : {1}, count : {2}", craftId, objId, count);

            var craft = CraftManager.Instance.GetCraftById(craftId);
            var character = Connection.ActiveChar;
            character.Craft.Craft(craft, count, objId);
        }
    }
}
