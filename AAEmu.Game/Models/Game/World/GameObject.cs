using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Error;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World
{
    public class GameObject
    {
        public Guid Guid { get; set; } = Guid.NewGuid();
        public uint ObjId { get; set; }
        public uint InstanceId { get; set; } = 1;
        public bool DisabledSetPosition { get; set; }
        /// <summary>
        /// Contains position, rotation, zone and instance information
        /// </summary>
        public Transform Transform { get; set; }
        //public Point Position { get; set; }
        /// <summary>
        /// When not null, this is the location where the character last entered a instance from
        /// </summary>
        public Transform MainWorldPosition { get; set; }
        public Region Region { get; set; }
        public DateTime Respawn { get; set; }
        public DateTime Despawn { get; set; }
        public virtual bool IsVisible { get; set; }
        public GameObject ParentObj { get; set; }
        public virtual float ModelSize { get; set; } = 0f; 

        public GameObject()
        {
            Transform = new Transform(this,null);
        }

        public virtual void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ)
        {
            if (DisabledSetPosition)
                return;

            Transform.Local.SetPosition(x,y,z,rotationX,rotationY,rotationZ);
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
