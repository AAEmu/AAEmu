using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuyCoinItemPacket : GamePacket
    {
        public CSBuyCoinItemPacket() : base(0x0af, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var id = stream.ReadUInt32();

            _log.Trace("BuyCoinItem, objId: {0}, id: {1}", objId, id);
            var doodad = WorldManager.Instance.GetDoodad(objId);
            if (doodad == null)
            {
                _log.Warn("BuyCoinItem, no such doodad objId: {0}", objId);
                return;
            }
            var doodadFunc = DoodadManager.Instance.GetFunc(id);
            if (doodadFunc == null)
            {
                _log.Warn("BuyCoinItem, no doodadFunc for objId: {0}, id: {1}", objId, id);
                return;
            }
            var funcTemplate = DoodadManager.Instance.GetFuncTemplate(doodadFunc.FuncId, doodadFunc.FuncType);
            if (funcTemplate == null)
            {
                _log.Warn("BuyCoinItem, no funcTemplate found with doodad func for objId: {0}, id: {1} funcId: {2}, funcType: {3}", objId, id, doodadFunc.FuncId, doodadFunc.FuncType);
                return;
            }
            funcTemplate.Use(Connection.ActiveChar, doodad, 0);
        }
    }
}
