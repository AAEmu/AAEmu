using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.World
{
    public class GameObject
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public uint BcId { get; set; }
        public Point Position { get; set; }
        public Region Region { get; set; }

        public virtual bool IsVisible { get; set; }

        public virtual void SetPosition(Point pos)
        {
            Position = pos.Clone();
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void SetPosition(float x, float y, float z)
        {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Position.RotationX = rotationX;
            Position.RotationY = rotationY;
            Position.RotationZ = rotationZ;
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void Spawn()
        {
            WorldManager.Instance.AddObject(this);
            Show();
        }

        public virtual void Delete()
        {
            Hide();
            WorldManager.Instance.RemoveObject(this);
        }

        public virtual void Show()
        {
            IsVisible = true;
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void Hide()
        {
            IsVisible = false;
            WorldManager.Instance.RemoveVisibleObject(this);
        }

        public virtual void BroadcastPacket(GamePacket packet, bool self)
        {
        }

        public virtual void AddVisibleObject(Character character)
        {
        }

        public virtual void RemoveVisibleObject(Character character)
        {
        }
    }
}