using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class Doodad : BaseUnit
    {
        private float _scale;

        public uint TemplateId { get; set; }
        public DoodadTemplate Template { get; set; }
        public override float Scale => _scale;
        public uint FuncGroupId { get; set; }
        public uint ItemId { get; set; }
        public DateTime GrowthTime { get; set; }
        public DateTime PlantTime { get; set; }
        public DoodadOwnerType OwnerType { get; set; }
        public uint OwnerBcId { get; set; }
        public uint OwnerId { get; set; }

        public DoodadSpawner Spawner { get; set; }

        public uint TimeLeft => 0u; // TODO formula time of phase

        public Doodad()
        {
            _scale = 1f;
            Position = new Point();
            PlantTime = DateTime.MinValue;
        }

        public void SetScale(float scale)
        {
            _scale = scale;
        }

        public uint GetGroupId()
        {
            foreach(var funcGroup in Template.FuncGroups)
            {
                if(funcGroup.GroupKindId == 1)
                    return funcGroup.Id;
            }
            return 0;
        }
        
        public override void BroadcastPacket(GamePacket packet, bool self)
        {
            foreach (var character in WorldManager.Instance.GetAround<Character>(this))
                character.SendPacket(packet);
        }

        public override void AddVisibleObject(Character character)
        {
            character.SendPacket(new SCDoodadsCreatedPacket(new[] {this}));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.BcId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] {BcId}));
        }
    }
}