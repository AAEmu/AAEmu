using AAEmu.Commons.Network;
using AAEmu.Game.Core.Helper;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExecuteCraft : GamePacket
    {
        public CSExecuteCraft() : base(0x0f5, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var craftId = stream.ReadUInt32();
            var objId = stream.ReadBc();
            var count = stream.ReadInt32();

            _log.Debug("CSExecuteCraft, craftId : {0} , objId : {1}, count : {2}", craftId, objId, count);
        
            Craft craft = CraftManager.Instance.GetCraftById(craftId);
            Character character = Connection.ActiveChar;
            character.Craft.Craft(craft, count, objId);
        }
    }
}
