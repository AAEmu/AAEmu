using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInteractNPCEndPacket : GamePacket
    {
        public CSInteractNPCEndPacket() : base(CSOffsets.CSInteractNPCEndPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();

            Connection.ActiveChar.CurrentNPC = null;

            _log.Debug("InteractNPCEnd, BcId: {0}", objId);
        }
    }
}
