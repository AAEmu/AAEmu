using System;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.World
{
    public interface IGameObject
    {
        DateTime Despawn { get; set; }
        bool DisabledSetPosition { get; set; }
        Guid Guid { get; set; }
        uint InstanceId { get; set; }
        bool IsVisible { get; set; }
        Transform.Transform MainWorldPosition { get; set; }
        float ModelSize { get; set; }
        uint ObjId { get; set; }
        IGameObject ParentObj { get; set; }
        Region Region { get; set; }
        DateTime Respawn { get; set; }
        Transform.Transform Transform { get; set; }

        void AddVisibleObject(ICharacter character);
        void BroadcastPacket(GamePacket packet, bool self);
        string DebugName();
        void Delete();
        void Hide();
        void RemoveVisibleObject(ICharacter character);
        void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ);
        void Show();
        void Spawn();
    }
}
