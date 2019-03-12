using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using Point = AAEmu.Game.Models.Game.World.Point;

namespace AAEmu.Game.Models.Game.Team
{
    public class TeamMember : PacketMarshaler
    {
        public Character Character { get; }
        public MemberRole Role { get; set; }

        public TeamMember(Character character = null)
        {
            Character = character;
            Role = MemberRole.Undecided;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Character.Id);
            stream.Write(Character.Name);
            stream.Write((byte)Character.Race);
            stream.Write((byte)Character.Gender);
            stream.Write(Character.Level);
            stream.Write((byte)Role);
            stream.WriteBc(Character.ObjId);
            return stream;
        }

        public PacketStream WritePerson(PacketStream stream)
        {
            stream.Write(Character.Id);
            stream.Write((ulong)0); // zi
            stream.Write(Character.Level);
            stream.Write(Character.Hp);
            stream.Write(Character.MaxHp);
            stream.Write(Character.Mp);
            stream.Write(Character.MaxMp);
            stream.Write(Helpers.ConvertX(Character.Position.X));
            stream.Write(Helpers.ConvertX(Character.Position.Y));
            stream.Write(Helpers.ConvertZ(Character.Position.Z));
            stream.Write(MathUtil.ConvertDirectionToDegree(Character.Position.RotationZ)); // angZ
            stream.Write((byte)Character.Ability1);
            stream.Write((byte)Character.Ability2);
            stream.Write((byte)Character.Ability3);
            stream.Write(!Character.IsOnline);
            return stream;
        }
    }
}
