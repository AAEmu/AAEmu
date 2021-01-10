using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTrialInfoPacket : GamePacket
    {
        private readonly uint _type;
        private readonly int _crimePoint;
        private readonly int _arrest;
        private readonly int _acceptGuilty;
        private readonly int _acceptTrial;
        private readonly int _notGuilty;
        private readonly int _guilty;
        private readonly int _total;
        private readonly int _cur;
        private readonly int _crimeRecord;
        private readonly int _botReport;
        public SCTrialInfoPacket(uint type, int crimePoint, int arrest, int acceptGuilty, int acceptTrial, int notGuilty, int guilty, int total, int cur, int crimeRecord, int botReport) 
            : base(SCOffsets.SCTrialInfoPacket, 5)
        {
            _type = type;
            _crimePoint = crimePoint;
            _arrest = arrest;
            _acceptGuilty = acceptGuilty;
            _acceptTrial = acceptTrial;
            _notGuilty = notGuilty;
            _guilty = guilty;
            _total = total;
            _cur = cur;
            _crimeRecord = crimeRecord;
            _botReport = botReport;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type);
            stream.Write(_crimePoint);
            stream.Write(_arrest);
            stream.Write(_acceptGuilty);
            stream.Write(_acceptTrial);
            stream.Write(_notGuilty);
            stream.Write(_guilty);
            stream.Write(_total);
            stream.Write(_cur);
            stream.Write(_crimeRecord);
            stream.Write(_botReport);
            return stream;
        }

    }

}
