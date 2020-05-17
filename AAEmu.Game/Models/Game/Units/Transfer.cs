using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Transfers;

using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Transfer : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;
        public TransferTemplate Template { get; set; }
        public Character Bounded { get; set; }
        public TransferSpawner Spawner { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }

        public Transfer()
        {
            Name = "";
            Equip = new Item[28];
        }

        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
            {
                character.SendPacket(packet);
            }
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCUnitStatePacket(this));
            character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));
        }
    }
}
