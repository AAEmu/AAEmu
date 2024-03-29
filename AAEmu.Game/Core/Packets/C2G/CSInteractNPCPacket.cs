using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInteractNPCPacket : GamePacket
{
    public CSInteractNPCPacket() : base(CSOffsets.CSInteractNPCPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var isTargetChanged = stream.ReadBoolean();

        Logger.Debug("InteractNPC, BcId: {0}, TargetChanged: {1}", objId, isTargetChanged);

        var unit = objId > 0 ? WorldManager.Instance.GetUnit(objId) : null;

        Connection.ActiveChar.CurrentInteractionObject = unit;

        if (isTargetChanged)
        {
            Connection.ActiveChar.CurrentTarget = unit;
        }

        Connection.SendPacket(new SCAiAggroPacket(objId, 0)); // TODO проверить count=1
    }
}
