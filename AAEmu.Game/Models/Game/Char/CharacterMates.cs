using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterMates
    {
        /*
         * TODO:
         * EQUIPMENT CHANGE
         * FINISH ATTRIBUTES
         * NAME FROM LOCALIZED TABLE
         */

        public Character Owner { get; set; }

        private Dictionary<ulong, MateDb> _mates; // itemId, MountDb
        private readonly List<uint> _removedMates;

        public CharacterMates(Character owner)
        {
            Owner = owner;
            _mates = new Dictionary<ulong, MateDb>();
            _removedMates = new List<uint>();
        }

        private MateDb GetMateInfo(ulong itemId)
        {
            return _mates.ContainsKey(itemId) ? _mates[itemId] : null;
        }

        private MateDb CreateNewMate(ulong itemId, string name)
        {
            if (_mates.ContainsKey(itemId)) return null;
            var template = new MateDb
            {
                // TODO
                Id = MateIdManager.Instance.GetNextId(),
                ItemId = itemId,
                Level = 50,
                Name = name,
                Owner = Owner.Id,
                Mileage = 0,
                Xp = ExpirienceManager.Instance.GetExpForLevel(50, true),
                Hp = 999999,
                Mp = 999999,
                UpdatedAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            _mates.Add(template.ItemId, template);
            return template;
        }

        public void SpawnMount(SkillItem skillData)
        {
            if (MateManager.Instance.GetActiveMate(Owner.ObjId) != null)
            {
                DespawnMate(0);
                return;
            }

            var item = Owner.Inventory.GetItem(skillData.ItemId);
            if (item == null) return;

            var itemTemplate = (SummonMateTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
            var npcId = itemTemplate.NpcId;
            var template = NpcManager.Instance.GetTemplate(npcId);
            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = ObjectIdManager.Instance.GetNextId();
            var mateDbInfo = GetMateInfo(skillData.ItemId) ?? CreateNewMate(skillData.ItemId, template.Name); // TODO - new name

            var mount = new Mount
            {
                ObjId = objId,
                TlId = tlId,
                OwnerId = Owner.Id,
                Name = mateDbInfo.Name,
                TemplateId = template.Id,
                Template = template,
                ModelId = template.ModelId,
                Faction = Owner.Faction,
                Level = (byte)mateDbInfo.Level,
                Hp = mateDbInfo.Hp,
                Mp = mateDbInfo.Mp,
                Position = Owner.Position.Clone(),
                OwnerObjId = Owner.ObjId,

                Id = mateDbInfo.Id,
                ItemId = mateDbInfo.ItemId,
                UserState = 1, // TODO
                Exp = mateDbInfo.Xp,
                Mileage = mateDbInfo.Mileage,
                SpawnDelayTime = 0, // TODO
            };
            foreach (var skill in MateManager.Instance.GetMateSkills(npcId))
            {
                mount.Skills.Add(skill);
            }

            var (newX, newY) = MathUtil.AddDistanceToFront(3, mount.Position.X, mount.Position.Y, mount.Position.RotationZ);
            mount.Position.X = newX;
            mount.Position.Y = newY;

            MateManager.Instance.AddActiveMateAndSpawn(Owner, mount, item);
        }

        public void DespawnMate(uint tlId)
        {
            var mateInfo = MateManager.Instance.GetActiveMateByTlId(tlId);
            if (mateInfo != null)
            {
                var mateDbInfo = GetMateInfo(mateInfo.ItemId);
                if (mateDbInfo != null)
                {
                    mateDbInfo.Hp = mateInfo.Hp;
                    mateDbInfo.Mp = mateInfo.Mp;
                    mateDbInfo.Level = mateInfo.Level;
                    mateDbInfo.Xp = mateInfo.Exp;
                    mateDbInfo.Mileage = mateInfo.Mileage;
                    mateDbInfo.Name = mateInfo.Name;
                    mateDbInfo.UpdatedAt = DateTime.Now;
                }
            }

            MateManager.Instance.RemoveActiveMateAndDespawn(Owner, tlId);
        }

        public void Load(GameDBContext ctx)
        {
            _mates = _mates.Concat(
                ctx.Mates
                .Where(m => m.Owner == Owner.Id)
                .ToList()
                .Select(m => (MateDb)m)
                .ToDictionary(m => m.ItemId, m => m)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            if (_removedMates.Count > 0)
            {
                ctx.Mates.RemoveRange(
                    ctx.Mates.Where(m => m.Owner == Owner.Id && _removedMates.Contains((uint)m.Id)));
                _removedMates.Clear();
            }
            ctx.SaveChanges();

            foreach (var value in _mates.Values)
            {
                ctx.Mates.RemoveRange(
                    ctx.Mates.Where(m =>
                        m.Id == value.Id &&
                        m.ItemId == value.ItemId &&
                        m.Owner == value.Owner));
            }
            ctx.SaveChanges();

            ctx.Mates.AddRange(_mates.Values.Select(m => m.ToEntity()));

            ctx.SaveChanges();
        }
    }

    public class MateDb
    {
        public uint Id { get; set; }
        public ulong ItemId { get; set; }
        public string Name { get; set; }
        public int Xp { get; set; }
        public ushort Level { get; set; }
        public int Mileage { get; set; }
        public int Hp { get; set; }
        public int Mp { get; set; }
        public uint Owner { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public Mates ToEntity()
            =>
            new Mates()
            {
                Id        = this.Id        ,
                ItemId    = this.ItemId    ,
                Name      = this.Name      ,
                Xp        = this.Xp        ,
                Level     = this.Level     ,
                Mileage   = this.Mileage   ,
                Hp        = this.Hp        ,
                Mp        = this.Mp        ,
                Owner     = this.Owner     ,
                UpdatedAt = this.UpdatedAt ,
                CreatedAt = this.CreatedAt ,
            };

        public static explicit operator MateDb(Mates v)
            =>
            new MateDb
            {
                Id        = v.Id        ,
                ItemId    = v.ItemId    ,
                Name      = v.Name      ,
                Xp        = v.Xp        ,
                Level     = v.Level     ,
                Mileage   = v.Mileage   ,
                Hp        = v.Hp        ,
                Mp        = v.Mp        ,
                Owner     = v.Owner     ,
                UpdatedAt = v.UpdatedAt ,
                CreatedAt = v.CreatedAt ,
            };
    }
}
