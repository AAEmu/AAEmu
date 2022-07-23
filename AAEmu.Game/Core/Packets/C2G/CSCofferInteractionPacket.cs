using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCofferInteractionPacket : GamePacket
    {
        public CSCofferInteractionPacket() : base(CSOffsets.CSCofferInteractionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var cofferObjId = stream.ReadBc();
            var opening = stream.ReadBoolean();
            
            _log.Warn("CofferInteraction, cofferObjId: {0}, opening: {1}", cofferObjId, opening);
            if (opening)
            {
                if (!DoodadManager.Instance.OpenCofferDoodad(Connection.ActiveChar, cofferObjId))
                {
                    _log.Warn($"{Connection.ActiveChar.Name} failed to Open coffer objId {cofferObjId}");
                    // If it failed, the coffer is likely in use by somebody else
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.CofferInUse);
                }
            }
            else
            {
                if (!DoodadManager.Instance.CloseCofferDoodad(Connection.ActiveChar, cofferObjId))
                    _log.Warn($"{Connection.ActiveChar.Name} failed to Close coffer objId {cofferObjId}");
            }
        }
    }
}
