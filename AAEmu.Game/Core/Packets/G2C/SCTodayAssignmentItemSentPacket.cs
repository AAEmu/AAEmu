using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTodayAssignmentItemSentPacket : GamePacket
    {
        private readonly uint _todayAssignmentGoal;
        private readonly bool _byMail;

        public SCTodayAssignmentItemSentPacket(uint todayAssignmentGoal, bool byMail)
            : base(SCOffsets.SCTodayAssignmentItemSentPacket, 5)
        {
            _todayAssignmentGoal = todayAssignmentGoal;
            _byMail = byMail;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_todayAssignmentGoal);
            stream.Write(_byMail);
            return stream;
        }
    }
}
