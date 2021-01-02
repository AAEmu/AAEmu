using System;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.World
{
    public class PositionAndRotation
    {
        public Vector3 Position;
        public Quaternion Rotation;

        private const float ToShortDivider = (1f / 32768f); // 0.000030518509f ;
        private const float ToSByteDivider = (1f / 127f); // 0.000030518509f ;

        public PositionAndRotation()
        {
            Position = new Vector3();
            Rotation = new Quaternion();
        }

        public PositionAndRotation(float posX, float posY, float posZ, float rotX, float rotY, float rotZ, float rotW)
        {
            Position = new Vector3(posX, posY, posZ);
            Rotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public PositionAndRotation(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public PositionAndRotation Clone()
        {
            return new PositionAndRotation(Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z, Rotation.W);
        }

        public Vector3 ToYawPitchRoll()
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

        internal void SetPosition(float x, float y, float z)
        {
            Position = new Vector3(x, y, z);
        }
        internal void SetPosition(float x, float y, float z, float yaw, float pitch, float roll)
        {
            Position = new Vector3(x, y, z);
            Rotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }

        public (short,short,short) ToYawPitchRollShorts()
        {
            // ((short) = ((float)reverseQuat.X / 0.00003052f)
            // Still needs a Quaternion.Normalize maybe ?
            short x = (short)(Rotation.X / ToShortDivider);
            short y = (short)(Rotation.Y / ToShortDivider);
            short z = (short)(Rotation.Z / ToShortDivider);
            return (x,y,z);
        }

        public (sbyte, sbyte, sbyte) ToYawPitchRollSBytes()
        {
            // Still needs a Quaternion.Normalize maybe ?
            sbyte x = (sbyte)(Rotation.X / ToSByteDivider);
            sbyte y = (sbyte)(Rotation.Y / ToSByteDivider);
            sbyte z = (sbyte)(Rotation.Z / ToSByteDivider);
            return (x, y, z);
        }

        public void SetZRotation(float rotZ)
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rotZ);
        }

        public void SetZRotation(short rotZ)
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)MathUtil.ConvertDirectionToRadian(Helpers.ConvertRotation(rotZ)));
        }

        public void SetZRotation(sbyte rotZ)
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)MathUtil.ConvertDirectionToRadian(rotZ));
        }

        public void Translate(Vector3 offset)
        {
            Position += offset;
        }

        public void Translate(float offsetX, float offsetY, float offsetZ)
        {
            Position += new Vector3(offsetX, offsetY, offsetZ);
        }

        public void Rotate(Quaternion offset)
        {
            // Is this correct ?
            Rotation *= offset;
        }

        /// <summary>
        /// Adds distance in front (only takes into account Z direction, roll)
        /// </summary>
        /// <param name="distance"></param>
        public void AddDistanceToFront(float distance)
        {
            var ypr = ToYawPitchRoll();
            var off = new Vector3((distance * (float)Math.Cos(ypr.Z)), (distance * (float)Math.Sin(ypr.Z)), 0);
            Translate(off);
        }

        public void AddDistanceToRight(float distance)
        {
            var ypr = ToYawPitchRoll();
            var z = ypr.Z - (MathF.PI / 2);
            var off = new Vector3((distance * (float)Math.Cos(z)), (distance * (float)Math.Sin(z)), 0);
            Translate(off);
        }

        public Vector3 ClonePosition()
        {
            return new Vector3(Position.X,Position.Y,Position.Z);
        }
    }

    /// <summary>
    /// Helper Class to help manipulating GameObjects positions in 3D space
    /// </summary>
    public class Transform : IDisposable
    {
        private GameObject _owningObject;
        private uint _worldId ;
        private uint _instanceId ;
        private uint _zoneId;
        private PositionAndRotation _localPosRot;
        private Transform _parentTransform;
        private List<Transform> _children;

        /// <summary>
        /// Parent Transform this Transform is attached to, leave null for World
        /// </summary>
        public Transform Parent { get => _parentTransform; set { SetParent(value); } }
        /// <summary>
        /// List of Child Transforms of this Transform
        /// </summary>
        public List<Transform> Children { get => _children; }
        /// <summary>
        /// The GameObject this Transform is attached to
        /// </summary>
        public GameObject GameObject { get => _owningObject; }
        /// <summary>
        /// World ID
        /// </summary>
        public uint WorldId { get => _worldId; set => _worldId = value; }
        /// <summary>
        /// Instance ID
        /// </summary>
        public uint InstanceId { get => _instanceId; set => _instanceId = value; }
        /// <summary>
        /// Zone ID (Key)
        /// </summary>
        public uint ZoneId { get => _zoneId; set => _zoneId = value; }
        public Vector3 LocalPosition { get => _localPosRot.Position; set => _localPosRot.Position = value; }
        public Quaternion LocalRotation { get => _localPosRot.Rotation; set => _localPosRot.Rotation = value; }
        /// <summary>
        /// The Local Transform information (relative to Parent)
        /// </summary>
        public PositionAndRotation Local { get => _localPosRot; }
        public Vector3 WorldPosition { get => GetWorldPosition().Position; }
        public Quaternion WorldRotation { get => GetWorldPosition().Rotation; }
        /// <summary>
        /// The Global Transform information (relative to game world)
        /// </summary>
        public PositionAndRotation World { get => GetWorldPosition(); }

        protected void InternalInitializeTransform(GameObject owningObject, Transform parentTransform = null)
        {
            _worldId = 1;
            _instanceId = 1;
            _owningObject = owningObject;
            _parentTransform = parentTransform;
            _children = new List<Transform>();
            _localPosRot = new PositionAndRotation();
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
            InstanceId = instanceId;
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = new Quaternion(rotX, rotY, rotZ, rotW);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float rotZ)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, rotZ);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float yaw, float pitch, float roll)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            LocalPosition = new Vector3(posX, posY, posZ);
            LocalRotation = Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, PositionAndRotation posRot)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            _localPosRot = new PositionAndRotation(posRot.Position, posRot.Rotation);
        }

        /// <summary>
        /// Clones a Transform including GameObject and Parent Transform information
        /// </summary>
        /// <returns></returns>
        public Transform Clone()
        {
            return new Transform(_owningObject, _parentTransform, WorldId, ZoneId, InstanceId, _localPosRot);
        }
        public Transform Clone(GameObject newOwner)
        {
            return new Transform(newOwner, _parentTransform, WorldId, ZoneId, InstanceId, _localPosRot);
        }

        /// <summary>
        /// Clones a Transform without GameObject or Parent Transform, using the current World relative position
        /// </summary>
        /// <returns></returns>
        public Transform CloneDetached()
        {
            return new Transform(null, null, WorldId, ZoneId, InstanceId, GetWorldPosition());
        }

        public Transform CloneDetached(GameObject newOwner)
        {
            return new Transform(newOwner, null, WorldId, ZoneId, InstanceId, GetWorldPosition());
        }

        /// <summary>
        /// Clones a Transform using childObject as new owner and setting Parent Transform to the current transform, the new clone has a local position initialized as 0,0,0
        /// </summary>
        /// <returns></returns>
        public Transform CloneAttached(GameObject childObject)
        {
            return new Transform(childObject, this, WorldId, ZoneId, InstanceId, new PositionAndRotation());
        }

        public WorldSpawnPosition CloneAsSpawnPosition()
        {
            var ypr = this.World.ToYawPitchRoll();
            return new WorldSpawnPosition()
            {
                WorldId = this.WorldId,
                ZoneId = this.ZoneId,
                X = this.World.Position.X,
                Y = this.World.Position.Y,
                Z = this.World.Position.Z,
                Yaw = ypr.X,
                Pitch = ypr.Y,
                Roll = ypr.Z
            };
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
            if ((parent == null) || (!parent.Equals(_parentTransform)))
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

        protected PositionAndRotation GetWorldPosition()
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

        /// <summary>
        /// Detaches the transform, and moves set the Local position to what is defined in the WorldSpawnPosition
        /// </summary>
        /// <param name="wsp">WorldSpawnPosition to copy information from</param>
        /// <param name="newInstanceId">new InstanceId to assign to this transform, unchanged if 0</param>
        public void ApplyWorldSpawnPosition(WorldSpawnPosition wsp,uint newInstanceId = 0)
        {
            DetachAll();
            WorldId = wsp.WorldId;
            ZoneId = wsp.ZoneId;
            if (newInstanceId != 0)
                InstanceId = newInstanceId;
            Local.Position = new Vector3(wsp.X, wsp.Y, wsp.Z);
            Local.Rotation = Quaternion.CreateFromYawPitchRoll(wsp.Yaw, wsp.Pitch, wsp.Roll);
        }
    }
}
