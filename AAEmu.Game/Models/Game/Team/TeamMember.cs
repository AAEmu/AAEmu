using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Team
{
    public class TeamMember : PacketMarshaler
    {
        public Character Member { get; set; }
        public MemberRole Role { get; set; }

        public TeamMember(Character character = null)
        {
            Member = character;
            Role = MemberRole.Undecided;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Member.Id);
            stream.Write(Member.Name);
            stream.Write((byte)Member.Race);
            stream.Write((byte)Member.Gender);
            stream.Write(Member.Level);
            stream.Write((byte)Role);
            stream.WriteBc(Member.ObjId); // uid

            return stream;
        }

        public PacketStream WritePerson(PacketStream stream)
        {
            stream.Write(Member.Id);
            stream.Write((ulong)0); // zi
            stream.Write(Member.Level);
            stream.Write(Member.Hp);
            stream.Write(Member.MaxHp);
            stream.Write(Member.Mp);
            stream.Write(Member.MaxMp);
            stream.WritePosition(Member.Position.X, Member.Position.Y, Member.Position.Z);
            stream.Write(MathUtil.ConvertDirectionToDegree(Member.Position.RotationZ)); // angZ
            stream.Write((byte)Member.Ability1);
            stream.Write((byte)Member.Ability2);
            stream.Write((byte)Member.Ability3);
            stream.Write(!Member.IsOnline);

            return stream;
        }
    }
}
