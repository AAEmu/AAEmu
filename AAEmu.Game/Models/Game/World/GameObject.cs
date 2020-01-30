using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.World
{
    public class GameObject
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public uint ObjId { get; set; }
        public uint InstanceId { get; set; } = 1;
        public bool DisabledSetPosition { get; set; }
        public Point Position { get; set; }
        public Point WorldPosition { get; set; }
        public Region Region { get; set; }

        public DateTime Respawn { get; set; }
        public DateTime Despawn { get; set; }

        public virtual bool IsVisible { get; set; }

        public virtual void SetPosition(Point pos)
        {
            if (DisabledSetPosition)
                return;

            Position = pos.Clone();
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void SetPosition(float x, float y, float z)
        {
            if (DisabledSetPosition)
                return;

            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            WorldManager.Instance.AddVisibleObject(this);
        }

        public virtual void SetPosition(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
        {
            if (DisabledSetPosition)
                return;

            if (this is Character)
                if (!Position.X.Equals(x) || !Position.Y.Equals(y) || !Position.Z.Equals(z))
                    TeamManager.Instance.UpdatePosition(((Character)this).Id);

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
