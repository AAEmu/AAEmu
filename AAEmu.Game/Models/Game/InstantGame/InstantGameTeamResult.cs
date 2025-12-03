using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.InstantGame.Static;

namespace AAEmu.Game.Models.Game.InstantGame
{
    public class InstantGameTeamResult : PacketMarshaler
    {
        public VictoryState State { get; set; }
        public uint FactionId { get; set; }

        public int Score
        {
            get
            {
                return Members.Sum(m => m.Score);
            }
        }

        public short TotalKills
        {
            get
            {
                return (short)Members.Sum(m => m.Kills);
            }
        }

        public short TotalDeaths
        {
            get
            {
                return (short)Members.Sum(m => m.Deaths);
            }
        }

        public List<InstantGameTeamMember> Members { get; set; }

        public InstantGameTeamResult(VictoryState state, uint factionId)
        {
            State = state;
            FactionId = factionId;
            Members = new List<InstantGameTeamMember>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)State);
            stream.Write(FactionId);
            stream.Write(Score);
            stream.Write(TotalKills);
            stream.Write(TotalDeaths);
            stream.Write((byte)Members.Count);
            foreach (var member in Members)
            {
                stream.Write(member);
            }

            return stream;
        }
    }
}
