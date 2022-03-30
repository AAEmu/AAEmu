using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAskImprisonOrTrialPacket :GamePacket
    {
        private readonly uint _crimeValue;
        private readonly uint _jailMinutes;
        public SCAskImprisonOrTrialPacket(uint crimeValue, uint jailMinutes) : 
            base(SCOffsets.SCAskImprisonOrTrialPacket, 5)
        {
            _crimeValue = crimeValue;
            _jailMinutes = jailMinutes;
        }
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_crimeValue);
            stream.Write(_jailMinutes);
            return stream;
        }
    }
}
