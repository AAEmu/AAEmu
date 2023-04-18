using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSInteractNPCPacket : GamePacket
    {
        public CSInteractNPCPacket() : base(CSOffsets.CSInteractNPCPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var isTargetChanged = stream.ReadBoolean();

            _log.Debug("InteractNPC, BcId: {0}", objId);

            var unit = objId > 0 ? WorldManager.Instance.GetUnit(objId) : null;

            if (unit is Npc npc)
            {
                Connection.ActiveChar.CurrentNPC = npc;
            }

            Connection.SendPacket(new SCAiAggroPacket(objId, 0)); // TODO проверить count=1
        }
    }
}
