using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Team
{
    public class Team : PacketMarshaler
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public bool IsParty { get; set; }
        public TeamMember[] Members { get; set; }
        public LootingRule LootingRule { get; set; }
        public (byte, uint)[] MarksList { get; set; }
        public Point PingPosition { get; set; }

        public Team()
        {
            Members = new TeamMember[50];
            for (var i = 0; i < Members.Length; i++)
                Members[i] = new TeamMember();

            ResetMarks();
            PingPosition = new Point(0, 0, 0);
        }

        public void ResetMarks()
        {
            MarksList = new (byte, uint)[12];
            for (var i = 0; i < 12; i++)
            {
                MarksList[i] = (0, 0);
            }
        }

        public bool IsMarked(uint id)
        {
            foreach (var (_, obj) in MarksList)
            {
                if (obj == id) return true;
            }

            return false;
        }

        public int MembersCount()
        {
            var count = 0;
            for (var i = 0; i < Members.Length; i++)
            {
                if (Members[i].Character != null) count++;
            }

            return count;
        }

        public bool IsMember(uint id)
        {
            foreach (var member in Members)
            {
                if (member.Character != null && member.Character.Id == id) return true;
            }

            return false;
        }

        public uint GetNewOwner()
        {
            foreach (var member in Members)
            {
                if (member.Character != null && member.Character.IsOnline && member.Character.Id != OwnerId) return member.Character.Id;
            }

            return 0;
        }

        public bool ChangeRole(uint id, MemberRole role)
        {
            foreach (var member in Members)
            {
                if (member.Character?.Id != id) continue;

                if (member.Role == role) return false;
                member.Role = role;
                return true;
            }

            return false;
        }

        public (TeamMember, int) AddMember(Character unit)
        {
            for (var i = 0; i < Members.Length; i++)
            {
                if (Members[i].Character != null) continue;

                Members[i] = new TeamMember(unit);
                return (Members[i], GetParty(i));
            }

            return (null, 0);
        }

        public bool RemoveMember(uint id)
        {
            var i = GetIndex(id);
            if (i < 0) return false;

            Members[i] = new TeamMember();
            return true;
        }

        public bool MoveMember(uint id, uint id2, byte from, byte to)
        {
            try
            {
                var tempMember = Members[from];
                var tempMember2 = Members[to];
                Members[from] = tempMember2;
                Members[to] = tempMember;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public TeamMember ChangeStatus(Character unit)
        {
            var i = GetIndex(unit.Id);
            if (i < 0) return null;

            Members[i] = new TeamMember(unit);
            return Members[i];
        }

        public void BroadcastPacket(GamePacket packet, uint id = 0)
        {
            foreach (var member in Members)
            {
                if (member.Character == null || !member.Character.IsOnline || member.Character.Id == id) continue;
                member.Character.SendPacket(packet);
            }
        }

        public int GetIndex(uint id)
        {
            for (var i = 0; i < Members.Length; i++)
            {
                if (Members[i].Character != null && Members[i].Character.Id == id) return i;
            }

            return -1;
        }

        public int GetParty(int index)
        {
            if (index < 5) return 0;
            return index / 5;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(OwnerId);
            stream.Write(IsParty);

            // TODO - BETTER WAY TO MAKE THIS MONSTER
            var p1 = 0;
            var p2 = 0;
            var p3 = 0;
            var p4 = 0;
            var p5 = 0;
            var p6 = 0;
            var p7 = 0;
            var p8 = 0;
            var p9 = 0;
            var p10 = 0;
            for (var i = 0; i < Members.Length; i++)
            {
                if (Members[i].Character == null) continue;
                var party = GetParty(i);
                switch (party)
                {
                    case 0:
                        p1++;
                        break;
                    case 1:
                        p2++;
                        break;
                    case 2:
                        p3++;
                        break;
                    case 3:
                        p4++;
                        break;
                    case 4:
                        p5++;
                        break;
                    case 5:
                        p6++;
                        break;
                    case 6:
                        p7++;
                        break;
                    case 7:
                        p8++;
                        break;
                    case 8:
                        p9++;
                        break;
                    case 9:
                        p10++;
                        break;
                }
            }

            stream.Write((byte)p1);
            stream.Write((byte)p2);
            stream.Write((byte)p3);
            stream.Write((byte)p4);
            stream.Write((byte)p5);
            stream.Write((byte)p6);
            stream.Write((byte)p7);
            stream.Write((byte)p8);
            stream.Write((byte)p9);
            stream.Write((byte)p10);

            foreach (var member in Members)
            {
                stream.Write(member.Character?.Id ?? 0u);
                stream.Write(member.Character?.IsOnline ?? false);
            }

            for (var i = 0; i < 12; i++)
            {
                var type = MarksList[i].Item1;
                var obj = MarksList[i].Item2;
                stream.Write(type);
                if (type == 1)
                    stream.Write(obj);
                else if (type == 2)
                    stream.WriteBc(obj);
            }

            stream.Write(LootingRule);
            return stream;
        }
    }
}
