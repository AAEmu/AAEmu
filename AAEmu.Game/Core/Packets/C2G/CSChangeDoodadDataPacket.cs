using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeDoodadDataPacket : GamePacket
    {
        public CSChangeDoodadDataPacket() : base(CSOffsets.CSChangeDoodadDataPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var data = stream.ReadInt32();
            
            _log.Warn($"ChangeDoodadData, ObjId: {objId}, Data: {data}");
            var doodad = WorldManager.Instance.GetDoodad(objId);
            if (doodad != null)
            {
                var doodadName = LocalizationManager.Instance.Get("doodad_almighties", "name", doodad.TemplateId);
                var doodadType = doodad.Template.GetType().ToString();
                _log.Warn($"Doodad: {doodad.Name} ({doodad.TemplateId} - {doodadName} - {doodadType})");
                if (!DoodadManager.Instance.ChangeDoodadData(Connection.ActiveChar, doodad, data))
                    Connection.ActiveChar.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
            }
        }
    }
}
