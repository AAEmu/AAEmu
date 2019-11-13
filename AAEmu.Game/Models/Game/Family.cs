using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game
{
    public class Family : PacketMarshaler
    {
        private List<uint> _removedMembers;

        public uint Id { get; set; }
        public List<FamilyMember> Members { get; set; }

        public Family()
        {
            _removedMembers = new List<uint>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Members.Count); // TODO max length 8
            foreach (var member in Members)
                stream.Write(member);
            return stream;
        }

        public void AddMember(FamilyMember member)
        {
            if (Members == null)
                Members = new List<FamilyMember>();

            Members.Add(member);
        }

        public void RemoveMember(FamilyMember member)
        {
            Members.Remove(member);
            _removedMembers.Add(member.Id);
        }

        public void RemoveMember(Character character)
        {
            var member = GetMember(character);
            RemoveMember(member);
        }

        public FamilyMember GetMember(Character character)
        {
            foreach (var member in Members)
                if (member.Id == character.Id)
                    return member;

            return null;
        }

        public void SendPacket(GamePacket packet, uint exclude = 0)
        {
            foreach (var member in Members)
                if (member.Id != exclude)
                    member.Character?.SendPacket(packet);
        }

        public void Load(GameDBContext ctx)
        {
            Members = ctx.FamilyMembers
                .Where(f => f.FamilyId == Id)
                .ToList()
                .Select(f => (FamilyMember)f)
                .ToList();
        }

        public void Save(GameDBContext ctx)
        {
            if (_removedMembers.Count > 0)
            {
                
                ctx.FamilyMembers.RemoveRange(
                    ctx.FamilyMembers.Where(f => _removedMembers.Contains((uint)f.CharacterId)));
                ctx.SaveChanges();

                ctx.Characters.Where(c => _removedMembers.Contains((uint)c.Id))
                    .ToList()
                    .All(c => 
                        {
                            c.Family = 0;
                            return true;
                        });

                _removedMembers.Clear();
            }
            ctx.SaveChanges();

            ctx.FamilyMembers.RemoveRange(
                Members.SelectMany(m => 
                    ctx.FamilyMembers.Where(fm => fm.CharacterId == m.Id && fm.FamilyId == Id)));
            ctx.SaveChanges();

            ctx.FamilyMembers.AddRange(
                Members.Select(
                    m => {
                        var e = m.ToEntity();
                        e.FamilyId = Id;
                        return e;
                    }
                ).ToList());

            ctx.SaveChanges();
        }
    }

    public class FamilyMember : PacketMarshaler
    {
        public Character Character { get; set; }
        public uint Id { get; set; }
        public string Name { get; set; }
        public byte Role { get; set; }
        public bool Online => Character != null;
        public string Title { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name);
            stream.Write(Role);
            stream.Write(Online);
            stream.Write(Title);
            return stream;
        }

        public DB.Game.FamilyMembers ToEntity()
            =>
            new DB.Game.FamilyMembers()
            {
                CharacterId = this.Id    ,
                Name        = this.Name  ,
                Role        = this.Role  ,
                Title       = this.Title ,
            };

        public static explicit operator FamilyMember(FamilyMembers v)
            =>
            new FamilyMember()
            {
                Id    = v.CharacterId ,
                Name  = v.Name        ,
                Role  = v.Role        ,
                Title = v.Title       ,
            };
    }
}
