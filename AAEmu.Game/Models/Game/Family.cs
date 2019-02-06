using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game
{
    public class Family : PacketMarshaler
    {
        public uint Id { get; set; }
        public FamilyMember[] Members { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Members.Length); // TODO max length 8
            foreach (var member in Members)
                stream.Write(member);
            return stream;
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
