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

            _log.Debug("BuyCoinItem, objId: {0}, id: {1}", objId, id);
            var doodad = WorldManager.Instance.GetDoodad(objId);
            var doodadFunc = DoodadManager.Instance.GetFunc(id);
            var funcTemplate = DoodadManager.Instance.GetFuncTemplate(doodadFunc.FuncId, doodadFunc.FuncType);
            funcTemplate.Use(Connection.ActiveChar, doodad, 0);
        }
    }
}
