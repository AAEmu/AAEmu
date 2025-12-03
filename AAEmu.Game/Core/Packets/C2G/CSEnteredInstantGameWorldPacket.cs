using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEnteredInstantGameWorldPacket : GamePacket
    {
        private ulong _qualifiedId;

        public CSEnteredInstantGameWorldPacket() : base(CSOffsets.CSEnteredInstantGameWorldPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _qualifiedId = stream.ReadUInt64();

            Logger.Warn("EnteredInstantGameWorld, QualifiedId: {0}", _qualifiedId);
            // InstantGameManager.Instance.EnteredInstantGameWorld(Connection.ActiveChar, _qualifiedId);
            Connection.ActiveChar.CurrentInstantGame?.OnEnterWorld(Connection.ActiveChar, _qualifiedId);
        }
    }
}
