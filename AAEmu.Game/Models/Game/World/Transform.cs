using System;
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

        public Vector3 ToEulerAngles()
        {
            // Store the Euler angles in radians
            Vector3 yawPitchRoll = new Vector3();

            double sqw = Rotation.W * Rotation.W;
            double sqx = Rotation.X * Rotation.X;
            double sqy = Rotation.Y * Rotation.Y;
            double sqz = Rotation.Z * Rotation.Z;

            // If quaternion is normalised the unit is one, otherwise it is the correction factor
            double unit = sqx + sqy + sqz + sqw;
            double test = Rotation.X * Rotation.Y + Rotation.Z * Rotation.W;

            if (test > 0.4999f * unit)                              // 0.4999f OR 0.5f - EPSILON
            {
                // Singularity at north pole
                yawPitchRoll.X = 2f * (float)Math.Atan2(Rotation.X, Rotation.W);  // Yaw
                yawPitchRoll.Y = MathF.PI * 0.5f;                   // Pitch
                yawPitchRoll.Z = 0f;                                // Roll
                return yawPitchRoll;
            }
            else if (test < -0.4999f * unit)                        // -0.4999f OR -0.5f + EPSILON
            {
                // Singularity at south pole
                yawPitchRoll.X = -2f * (float)Math.Atan2(Rotation.X, Rotation.W); // Yaw
                yawPitchRoll.Y = -MathF.PI * 0.5f;                  // Pitch
                yawPitchRoll.Z = 0f;                                // Roll
                return yawPitchRoll;
            }
            else
            {
                yawPitchRoll.X = (float)Math.Atan2(2f * Rotation.Y * Rotation.W - 2f * Rotation.X * Rotation.Z, sqx - sqy - sqz + sqw);       // Yaw
                yawPitchRoll.Y = (float)Math.Asin(2f * test / unit);                                             // Pitch
                yawPitchRoll.Z = (float)Math.Atan2(2f * Rotation.X * Rotation.W - 2f * Rotation.Y * Rotation.Z, -sqx + sqy - sqz + sqw);      // Roll
            }

            return yawPitchRoll;
        }

        public void Translate(Vector3 offset)
        {
            Position += offset;
        }

        public void Rotate(Quaternion offset)
        {
            // Is this correct ?
            Rotation *= offset;
        }


        public void Translate(float offsetX, float offsetY, float offsetZ)
        {
            Position += new Vector3(offsetX,offsetY,offsetZ);
        }
    }

    public class Transform : IDisposable
    {
        private GameObject _owningObject;
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
        public PosistionAndRotation Local { get => _localPosRot; }
        public Vector3 WorldPosition { get => GetWorldPosition().Position; }
        public Quaternion WorldRotation { get => GetWorldPosition().Rotation; }
        public PosistionAndRotation World { get => GetWorldPosition(); }

        protected void InternalInitializeTransform(GameObject owningObject, Transform parentTransform = null)
        {
            _owningObject = owningObject;
            _parentTransform = parentTransform;
            _children = new List<Transform>();
            _localPosRot = new PosistionAndRotation();
        }

        public Transform(GameObject owningObject, Transform parentTransform)
        {
            InternalInitializeTransform(owningObject, parentTransform);
        }

        public Transform(GameObject owningObject, Transform parentTransform, float x, float y, float z)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            LocalPosition = new Vector3(x, y, z);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            LocalPosition = position;
        }

        public Transform(GameObject owningObject, Transform parentTransform, float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position, Quaternion rotation)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            LocalPosition = position;
            LocalRotation = rotation;
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, PosistionAndRotation posRot)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            _localPosRot = new PosistionAndRotation(posRot.Position, posRot.Rotation);
        }

        public Transform Clone()
        {
            return new Transform(_owningObject, _parentTransform, WorldId, ZoneId, InstanceId, _localPosRot);
        }

        public Transform CloneDetached()
        {
            return new Transform(null, null, WorldId, ZoneId, InstanceId, GetWorldPosition());
        }

        public Transform CloneAttached(GameObject childObject)
        {
            return new Transform(childObject, this, WorldId, ZoneId, InstanceId, new PosistionAndRotation());
        }

        ~Transform()
        {
            DetachAll();
        }

        public void Dispose()
        {
            DetachAll();
        }

        public void DetachAll()
        {
            Parent = null;
            foreach (var child in Children)
                child.Parent = null;
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

        private void InternalAttachChild(Transform child)
        {
            if (!_children.Contains(child))
            {
                _children.Add(child);
            }
        }

        private void InternalDetachChild(Transform child)
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
            var parentPosAndRot = _parentTransform.GetWorldPosition().Clone();
            // Is this even correct ?
            var parentPosMatrix = Matrix4x4.CreateTranslation(parentPosAndRot.Position);
            var parentRotMatrix = Matrix4x4.CreateFromQuaternion(parentPosAndRot.Rotation);
            // Combine parent pos and rot
            var resMatrix = Matrix4x4.Multiply(parentPosMatrix, parentRotMatrix);
            // Add local position and rotation
            var localPosMatrix = Matrix4x4.CreateTranslation(_localPosRot.Position);
            var localRotMatrix = Matrix4x4.CreateFromQuaternion(_localPosRot.Rotation);
            resMatrix = Matrix4x4.Multiply(resMatrix, localPosMatrix);
            resMatrix = Matrix4x4.Multiply(resMatrix, localRotMatrix);
            // Extract global location and split them again
            parentPosAndRot.Position = Vector3.Transform(LocalPosition, resMatrix);
            parentPosAndRot.Rotation = Quaternion.CreateFromRotationMatrix(resMatrix);
            return parentPosAndRot;
        }

    }
}
