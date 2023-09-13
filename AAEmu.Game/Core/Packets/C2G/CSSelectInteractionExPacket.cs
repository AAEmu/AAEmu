using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSelectInteractionExPacket : GamePacket
    {
        public CSSelectInteractionExPacket() : base(CSOffsets.CSSelectInteractionExPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var targetId = stream.ReadBc();
            var interactionEx = stream.ReadInt32();
            var var1 = stream.ReadInt32();

            _log.Warn("SelectInteractionEx, TargetId: {0}, InteractionEx: {1}, Var1: {2}", targetId, interactionEx, var1);
        }
    }
}
