using System.Collections.Generic;
using System.Numerics;

namespace AAEmu.Game.Models.Game.World
{
    public class PosistionAndRotation
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public PosistionAndRotation()
        {
            Position = new Vector3();
            Rotation = new Quaternion();
        }

        public PosistionAndRotation(float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            Position = new Vector3(posX, posY, posZ);
            Rotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public PosistionAndRotation(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public PosistionAndRotation Clone()
        {
            return new PosistionAndRotation(Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W);
        }
    }

    public class Transform
    {
        private GameObject _parentObject;
        private uint _worldId;
        private uint _instanceId;
        private uint _zoneId;
        private PosistionAndRotation _localPosRot;
        private Transform _parentTransform;
        private List<Transform> _children;

        public Transform Parent { get => _parentTransform; set { SetParent(value); } }
        public List<Transform> Children { get => _children; }
        public uint WorldId { get => _worldId; set => _worldId = value; }
        public uint InstanceId { get => _instanceId; set => _instanceId = value; }
        public uint ZoneId { get => _zoneId; set => _zoneId = value; }
        public Vector3 LocalPosition { get => _localPosRot.Position; set => _localPosRot.Position = value; }
        public Quaternion LocalRotation { get => _localPosRot.Rotation; set => _localPosRot.Rotation = value; }
        public Vector3 WorldPosition { get => GetWorldPosition().Position; }
        public Quaternion WorldRotation { get => GetWorldPosition().Rotation; }

        protected void InternalInitializeTransform(GameObject parentObject)
        {
            _parentObject = parentObject;
            _parentTransform = null;
            _children = new List<Transform>();
            _localPosRot = new PosistionAndRotation();
        }

        public Transform(GameObject parent)
        {
            InternalInitializeTransform(parent);
        }

        public Transform(GameObject parent, float x, float y, float z)
        {
            InternalInitializeTransform(parent);
            LocalPosition = new Vector3(x, y, z);
        }

        public Transform(GameObject parent, Vector3 position)
        {
            InternalInitializeTransform(parent);
            LocalPosition = position;
        }

        public Transform(GameObject parent, float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            InternalInitializeTransform(parent);
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public Transform(GameObject parent, Vector3 position, Quaternion rotation)
        {
            InternalInitializeTransform(parent);
            LocalPosition = position;
            LocalRotation = rotation;
        }

        public Transform(GameObject parent, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            InternalInitializeTransform(parent);
            WorldId = worldId;
            ZoneId = zoneId;
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public Transform(GameObject parent, uint worldId, uint zoneId, uint instanceId, PosistionAndRotation posRot)
        {
            InternalInitializeTransform(parent);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            _localPosRot = new PosistionAndRotation(posRot.Position, posRot.Rotation);
        }

        public Transform Clone()
        {
            return new Transform(_parentObject, WorldId, ZoneId, InstanceId, _localPosRot);
        }

        public Transform CloneDetached()
        {
            return new Transform(null, WorldId, ZoneId, InstanceId, GetWorldPosition());
        }

        protected void SetParent(Transform parent)
        {
            if (!parent.Equals(_parentTransform))
            {
                if (_parentTransform != null)
                    _parentTransform.InternalDetachChild(this);

                _parentTransform = parent;

                if (_parentTransform != null)
                    _parentTransform.InternalAttachChild(this);
            }
        }

        protected void InternalAttachChild(Transform child)
        {
            if (!_children.Contains(child))
            {
                _children.Add(child);
            }
        }

        protected void InternalDetachChild(Transform child)
        {
            if (_children.Contains(child))
            {
                _children.Remove(child);
            }
        }

        protected PosistionAndRotation GetWorldPosition()
        {
            if (_parentTransform == null)
                return _localPosRot;
            var wPos = _parentTransform.GetWorldPosition().Clone();
            // yea yea, I know this is wrong
            wPos.Position = wPos.Position + _localPosRot.Position;
            wPos.Rotation = wPos.Rotation + _localPosRot.Rotation;
            return wPos;
        }
    }
}
