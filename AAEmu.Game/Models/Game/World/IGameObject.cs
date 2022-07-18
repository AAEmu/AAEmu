using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Models.Game.World
{
    public interface IGameObject
    {
        uint ObjId { get; set; }
        Region Region { get; set; }
        Transform.Transform Transform { get; set; }
        void BroadcastPacket(GamePacket sCOneUnitMovementPacket, bool self);
    }
}
