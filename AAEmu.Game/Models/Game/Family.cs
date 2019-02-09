using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game
{
    public class Family : PacketMarshaler
    {
        public uint Id { get; set; }
        public List<FamilyMember> Members { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Members.Count); // TODO max length 8
            foreach (var member in Members)
                stream.Write(member);
            return stream;
        }

        public void AddMember(FamilyMember member) {
            if (Members == null)
                Members = new List<FamilyMember>();

            Members.Add(member);
        }

        public void RemoveMember(FamilyMember member) {
            Members.Remove(member);
        }

        public void RemoveMember(Character character) {
            Members.Remove(GetMember(character));
        }

        public FamilyMember GetMember(Character character) {
            foreach (FamilyMember member in Members) {
                if (member.Id == character.Id) return member;
            }
            return null;
        }
    }

    public class FamilyMember : PacketMarshaler
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public byte Role { get; set; }
        public bool Online { get; set; }
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
    }
}
