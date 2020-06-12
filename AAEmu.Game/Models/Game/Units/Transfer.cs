using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Transfers;

using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class Transfer : Unit
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public uint TemplateId { get; set; }
        public uint BondingObjId { get; set; } = 0;
        public byte AttachPointId { get; set; } = 0;
        public TransferTemplate Template { get; set; }
        public Character Bounded { get; set; }
        public TransferSpawner Spawner { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }
        public List<Doodad> AttachedDoodads { get; set; }

        public Transfer()
        {
            Name = "carriage";
            AttachedDoodads = new List<Doodad>();
            Template = new TransferTemplate();
            ModelParams = new UnitCustomModelParams();
            Bounded = new Character(ModelParams);
            Spawner = new TransferSpawner();
            ObjId = ObjectIdManager.Instance.GetNextId();
            TlId = (ushort)TlIdManager.Instance.GetNextId();
            Faction = FactionManager.Instance.GetFaction(143);
            Level = 1;
            Hp = 19000;
            Mp = 12000;
            TemplateId = 6;
            ModelId = 654;
        }

        public override void AddVisibleObject(Character character)
        {
            TransferManager.Instance.SpawnAll(character);
            //if (AttachedDoodads.Count > 0)
            //{
            //    var doodads = AttachedDoodads.ToArray();
            //    for (var i = 0; i < doodads.Length; i += 30)
            //    {
            //        var count = doodads.Length - i;
            //        var temp = new Doodad[count <= 30 ? count : 30];
            //        Array.Copy(doodads, i, temp, 0, temp.Length);
            //        character.SendPacket(new SCDoodadsCreatedPacket(temp));
            //    }
            //}
            //character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        }

        public override void RemoveVisibleObject(Character character)
        {
            if (character.CurrentTarget != null && character.CurrentTarget == this)
            {
                character.CurrentTarget = null;
                character.SendPacket(new SCTargetChangedPacket(character.ObjId, 0));
            }

            character.SendPacket(new SCUnitsRemovedPacket(new[] { ObjId }));

            var doodadIds = new uint[AttachedDoodads.Count];
            for (var i = 0; i < AttachedDoodads.Count; i++)
            {
                doodadIds[i] = AttachedDoodads[i].ObjId;
            }

            for (var i = 0; i < doodadIds.Length; i += 400)
            {
                var offset = i * 400;
                var length = doodadIds.Length - offset;
                var last = length <= 400;
                var temp = new uint[last ? length : 400];
                Array.Copy(doodadIds, offset, temp, 0, temp.Length);
                character.SendPacket(new SCDoodadsRemovedPacket(last, temp));
            }
        }
    }
}
