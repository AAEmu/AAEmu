using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterAbilities
    {
        public Dictionary<AbilityType, Ability> Abilities { get; set; }
        public Character Owner { get; set; }

        public CharacterAbilities(Character owner)
        {
            Owner = owner;
            Abilities = new Dictionary<AbilityType, Ability>();
            for (var i = 1; i < 11; i++)
            {
                var id = (AbilityType) i;
                Abilities[id] = new Ability(id);
            }
        }

        public IEnumerable<Ability> Values => Abilities.Values;

        public void SetAbility(AbilityType id, byte order)
        {
            Abilities[id].Order = order;
        }

        public List<AbilityType> GetActiveAbilities()
        {
            var list = new List<AbilityType>();
            if (Owner.Ability1 != AbilityType.None)
                list.Add(Owner.Ability1);
            if (Owner.Ability2 != AbilityType.None)
                list.Add(Owner.Ability2);
            if (Owner.Ability3 != AbilityType.None)
                list.Add(Owner.Ability3);
            return list;
        }

        public void AddExp(AbilityType type, int exp)
        {
            // TODO SCAbilityExpChangedPacket
            if (type != AbilityType.None)
                Abilities[type].Exp += exp;
        }

        public void AddActiveExp(int exp)
        {
            // TODO SCExpChangedPacket
            if (Owner.Ability1 != AbilityType.None)
                Abilities[Owner.Ability1].Exp += exp;
            if (Owner.Ability2 != AbilityType.None)
                Abilities[Owner.Ability2].Exp += exp;
            if (Owner.Ability3 != AbilityType.None)
                Abilities[Owner.Ability3].Exp += exp;
        }

        public void Swap(AbilityType oldAbilityId, AbilityType abilityId)
        {
            if (Owner.Ability1 == oldAbilityId)
            {
                Owner.Ability1 = abilityId;
                Abilities[abilityId].Order = 0;
            }
            else if (Owner.Ability2 == oldAbilityId)
            {
                Owner.Ability2 = abilityId;
                Abilities[abilityId].Order = 1;
            }
            else if (Owner.Ability3 == oldAbilityId)
            {
                Owner.Ability3 = abilityId;
                Abilities[abilityId].Order = 2;
            }

            if (oldAbilityId != AbilityType.None)
                Abilities[oldAbilityId].Order = 255;
            Owner.BroadcastPacket(new SCAbilitySwappedPacket(Owner.ObjId, oldAbilityId, abilityId), true);
        }

        public void Load(GameDBContext ctx)
        {
            Abilities = Abilities.Concat(ctx.Abilities
                .Where(a => a.Owner == Owner.Id)
                .ToList()
                .Select(a => (Ability)a)
                .Select(a =>
                {
                    a.Order = (byte)(a.Id == Owner.Ability1 ? 0 :
                                     a.Id == Owner.Ability2 ? 1 :
                                     a.Id == Owner.Ability3 ? 2 : 0);
                    return a;
                }).ToDictionary(a => a.Id, a => a))
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            List<byte> ids = Abilities.Keys.Select(a => (byte)(int)a).ToList();

            ctx.Abilities.RemoveRange(
                ctx.Abilities.Where(a => ids.Contains(a.Id) && a.Owner == Owner.Id));
            ctx.SaveChanges();

            ctx.Abilities.AddRange(
                Abilities.Values.Select(a => a.ToEntity(Owner.Id)));
            ctx.SaveChanges();
        }
    }
}
