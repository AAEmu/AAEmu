using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Intrinsics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using Quartz.Listener;

// INFO
// https://www.versluis.com/2020/09/what-is-yaw-pitch-and-roll-in-3d-axis-values/
// https://en.wikipedia.org/wiki/Euler_angles
// https://gamemath.com/
// https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles

namespace AAEmu.Game.Models.Game.World.Transform
{
    public class PositionAndRotation
    {
        public bool IsLocal { get; set; } = true;
        public Vector3 Position { get; set; }
        public Vector3 Rotation { get; set; }

        private const float ToShortDivider = (1f / 32768f); // ~0.000030518509f ;
        private const float ToSByteDivider = (1f / 127f);   // ~0.007874015748f ;

        public PositionAndRotation()
        {
            Position = new Vector3();
            Rotation = new Vector3();
        }

        public PositionAndRotation(float posX, float posY, float posZ, float rotX, float rotY, float rotZ)
        {
            Position = new Vector3(posX, posY, posZ);
            Rotation = new Vector3(rotX, rotY, rotZ);
        }

        public PositionAndRotation(Vector3 position, Vector3 rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public PositionAndRotation Clone()
        {
            return new PositionAndRotation(Position.X, Position.Y, Position.Z, Rotation.X, Rotation.Y, Rotation.Z);
        }

        public Vector3 ToRollPitchYawDegrees()
        {
            return new Vector3(Rotation.X.RadToDeg(), Rotation.Y.RadToDeg(), Rotation.Z.RadToDeg());
        }
        
        public void SetPosition(float x, float y, float z)
        {
            Position = new Vector3(x, y, z);
        }
        
        public void SetPosition(Vector3 pos)
        {
            Position = pos;
        }

        public void SetHeight(float z)
        {
            Position = new Vector3(Position.X, Position.Y, z);
        }
        
        public void SetPosition(float x, float y, float z, float roll, float pitch, float yaw)
        {
            Position = new Vector3(x, y, z);
            Rotation = new Vector3(roll, pitch, yaw);
        }

        public (short,short,short) ToRollPitchYawShorts()
        {
            short roll = (short)(Rotation.X / (MathF.PI * 2) / ToShortDivider);
            short pitch = (short)(Rotation.Y / (MathF.PI * 2) / ToShortDivider);
            short yaw = (short)(Rotation.Z / (MathF.PI * 2) / ToShortDivider);
            return (roll, pitch, yaw);
        }

        public (sbyte, sbyte, sbyte) ToRollPitchYawSBytes()
        {
            sbyte roll = (sbyte)(Rotation.X / (MathF.PI * 2) / ToSByteDivider);
            sbyte pitch = (sbyte)(Rotation.Y / (MathF.PI * 2) / ToSByteDivider);
            sbyte yaw = (sbyte)(Rotation.Z / (MathF.PI * 2) / ToSByteDivider);
            return (roll, pitch, yaw);
        }

        public (sbyte, sbyte, sbyte) ToRollPitchYawSBytesMovement()
        {
            sbyte roll =  MathUtil.ConvertRadianToDirection(Rotation.X - (MathF.PI / 2));
            sbyte pitch = MathUtil.ConvertRadianToDirection(Rotation.Y - (MathF.PI / 2));
            sbyte yaw =   MathUtil.ConvertRadianToDirection(Rotation.Z - (MathF.PI / 2));
            /*
            sbyte roll = (sbyte)(vec3.X / (Math.PI * 2) / ToSByteDivider);
            sbyte pitch = (sbyte)(vec3.Y / (Math.PI * 2) / ToSByteDivider);
            sbyte yaw = (sbyte)(vec3.Z / (Math.PI * 2) / ToSByteDivider);
            */
            return (roll, pitch, yaw);
        }
        
        public void SetRotation(float roll, float pitch, float yaw)
        {
            Rotation = new Vector3(roll, pitch, yaw);
        }
        
        public void SetRotationDegree(float roll, float pitch, float yaw)
        {
            Rotation = new Vector3(yaw.DegToRad(), pitch.DegToRad(), roll.DegToRad());
        }
        
        /// <summary>
        /// Sets Yaw in Radian
        /// </summary>
        /// <param name="rotZ"></param>
        public void SetZRotation(float rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, rotZ);
        }

        public void SetZRotation(short rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, (float)MathUtil.ConvertDirectionToRadian(Helpers.ConvertRotation(rotZ)));
        }

        public void SetZRotation(sbyte rotZ)
        {
            Rotation = new Vector3(Rotation.X, Rotation.Y, (float)MathUtil.ConvertDirectionToRadian(rotZ));
        }

        /// <summary>
        /// Move position by a given offset
        /// </summary>
        /// <param name="offset">Amount to offset</param>
        public void Translate(Vector3 offset)
        {
            Position += offset;
        }

        /// <summary>
        /// Move position by a given offset
        /// </summary>
        /// <param name="offsetX"></param>
        /// <param name="offsetY"></param>
        /// <param name="offsetZ"></param>
        public void Translate(float offsetX, float offsetY, float offsetZ)
        {
            // TODO: Take into account IsLocal = false
            Position += new Vector3(offsetX, offsetY, offsetZ);
        }

        public void Rotate(Vector3 offset)
        {
            // Is this correct ?
            Rotation += offset;
        }
        
        public void Rotate(float roll, float pitch, float yaw)
        {
            // Is this correct ?
            Rotation += new Vector3(roll, pitch, yaw);
        }
        
        /// <summary>
        /// Moves Transform forward by distance units
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="useFullRotation">When true, takes into account the full rotation instead of just on the horizontal pane. (not implemented yet)</param>
        public void AddDistanceToFront(float distance, bool useFullRotation = false)
        {
            // TODO: Use Quaternion to do a proper InFront, currently height is ignored
            // TODO: Take into account IsLocal = false
            var off = new Vector3((-distance * (float)Math.Sin(Rotation.Z)), (distance * (float)Math.Cos(Rotation.Z)), 0);
            Translate(off);
        }

        /// <summary>
        /// Moves Transform to it's right by distance units
        /// </summary>
        /// <param name="distance"></param>
        /// <param name="useFullRotation">When true, takes into account the full rotation instead of just on the horizontal pane. (not implemented yet)</param>
        public void AddDistanceToRight(float distance, bool useFullRotation = false)
        {
            // TODO: Use Quaternion to do a proper InFront, currently height is ignored
            // TODO: Take into account IsLocal = false
            var off = new Vector3((distance * (float)Math.Cos(Rotation.Z)), (distance * (float)Math.Sin(Rotation.Z)), 0);
            Translate(off);
        }
        
        /// <summary>
        /// Rotates Transform to make it face towards targetPosition's direction
        /// </summary>
        /// <param name="targetPosition"></param>
        public void LookAt(Vector3 targetPosition)
        {
            // TODO: Fix this as it's still wrong
            /*
            var forward = Vector3.Normalize(Position - targetPosition);
            var tmp = Vector3.Normalize(Vector3.UnitZ);
            var right = Vector3.Cross(tmp, forward);
            var up = Vector3.Cross(forward, right);
            var m = Matrix4x4.CreateLookAt(Position, targetPosition, up);
            var qr = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
            Rotation = qr;
            */
        }

        /// <summary>
        /// Clones current Position into a new Vector3
        /// </summary>
        /// <returns></returns>
        public Vector3 ClonePosition()
        {
            return new Vector3(Position.X,Position.Y,Position.Z);
        }

        public override string ToString()
        {
            return string.Format("X:{0:#,0.#} Y:{1:#,0.#} Z:{2:#,0.#}  r:{3:#,0.#}° p:{4:#,0.#}° y:{5:#,0.#}°",
                Position.X, Position.Y, Position.Z, Rotation.X.RadToDeg(), Rotation.Y.RadToDeg(), Rotation.Z.RadToDeg());
        }

        public bool IsOrigin()
        {
            return Position.Equals(Vector3.Zero);
        }
        
        /// <summary>
        /// Exports Rotation as a Quaternion
        /// </summary>
        /// <returns></returns>
        public Quaternion ToQuaternion() // yaw (Z), pitch (Y), roll (X)
        {
            // Abbreviations for the various angular functions
            var cy = MathF.Cos(Rotation.Z * 0.5f);
            var sy = MathF.Sin(Rotation.Z * 0.5f);
            var cp = MathF.Cos(Rotation.Y * 0.5f);
            var sp = MathF.Sin(Rotation.Y * 0.5f);
            var cr = MathF.Cos(Rotation.X * 0.5f);
            var sr = MathF.Sin(Rotation.X * 0.5f);

            Quaternion q;
            q.W = cr * cp * cy + sr * sp * sy;
            q.X = sr * cp * cy - cr * sp * sy;
            q.Y = cr * sp * cy + sr * cp * sy;
            q.Z = cr * cp * sy - sr * sp * cy;

            return q;
        }

        /// <summary>
        /// Sets Rotation from Quaternion values
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <param name="w"></param>
        public void FromQuaternion(float x, float y, float z, float w) {
            Vector3 angles;

            // roll (x-axis rotation)
            var sinRCosP = 2 * (w * x + y * z);
            var cosRCosP = 1 - 2 * (x * x + y * y);
            angles.X = MathF.Atan2(sinRCosP, cosRCosP);

            // pitch (y-axis rotation)
            var sinP = 2 * (w * y - z * x);
            angles.Y = MathF.Abs(sinP) >= 1 ? MathF.CopySign(MathF.PI / 2f, sinP) : MathF.Asin(sinP);

            // yaw (z-axis rotation)
            var sinYCosP = 2 * (w * z + x * y);
            var cosYCosP = 1 - 2 * (y * y + z * z);
            angles.Z = MathF.Atan2(sinYCosP, cosYCosP);

            Rotation = angles;
        }

        /// <summary>
        /// Sets Rotation using a Quaternion
        /// </summary>
        /// <param name="q"></param>
        public void FromQuaternion(Quaternion q)
        {
            FromQuaternion(q.X, q.Y, q.Z, q.W);
        }
        
    }

    /// <summary>
    /// Helper Class to help manipulating GameObjects positions in 3D space
    /// </summary>
    public class Transform : IDisposable
    {
        private GameObject _owningObject;
        private uint _worldId = WorldManager.DefaultWorldId ;
        private uint _instanceId = WorldManager.DefaultInstanceId;
        private uint _zoneId = 0;
        private PositionAndRotation _localPosRot;
        private Transform _parentTransform;
        private List<Transform> _children;
        private Vector3 _lastFinalizePos = Vector3.Zero; // Might use this later for cheat detection or delta movement

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
        /// <summary>
        /// The Local Transform information (relative to Parent)
        /// </summary>
        public PositionAndRotation Local { get => _localPosRot; }
        /// <summary>
        /// The Global Transform information (relative to game world)
        /// </summary>
        public PositionAndRotation World { get => GetWorldPosition(); }
        // TODO: It MIGHT be interesting to cache the world Transform, but would generate more overhead when moving parents (vehicles/mounts)

        private void InternalInitializeTransform(GameObject owningObject, Transform parentTransform = null)
        {
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
            Local.Position = new Vector3(x, y, z);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            Local.Position = position;
        }

        public Transform(GameObject owningObject, Transform parentTransform, float posX, float posY, float posZ, float roll, float pitch, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(roll, pitch, yaw);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position, Vector3 rotation)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            Local.Position = position;
            Local.Rotation = rotation;
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float roll, float pitch, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(roll, pitch, yaw);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(0f, 0f, yaw);
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
        
        /// <summary>
        /// Clones a Transform, keeps the parent Transform set, but replaces owning object with newOwner
        /// </summary>
        /// <param name="newOwner"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Clones a Transform without Parent Transform but with newOwner as new owner, using the current World relative position
        /// </summary>
        /// <param name="newOwner"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Clones the current World Transform into a WorldSpawnPosition object
        /// </summary>
        /// <returns></returns>
        public WorldSpawnPosition CloneAsSpawnPosition()
        {
            return new WorldSpawnPosition()
            {
                WorldId = this.WorldId,
                ZoneId = this.ZoneId,
                X = this.World.Position.X,
                Y = this.World.Position.Y,
                Z = this.World.Position.Z,
                Roll = this.World.Rotation.X,
                Pitch = this.World.Rotation.Y,
                Yaw = this.World.Rotation.Z
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

        /// <summary>
        /// Detaches this Transform from it's Parent, and detaches all it's children. Children get their World Transform as Local
        /// </summary>
        public void DetachAll()
        {
            Parent = null;
            foreach (var child in Children)
                child.Parent = null;
        }

        /// <summary>
        /// Assigns a new Parent Transform, automatically handles related child Transforms
        /// </summary>
        /// <param name="parent"></param>
        protected void SetParent(Transform parent)
        {
            if ((parent == null) || (!parent.Equals(_parentTransform)))
            {
                if (_parentTransform != null)
                    _parentTransform.InternalDetachChild(this);

                if ((_owningObject != null) && (_owningObject is Character player))
                {
                    var oldS = "<null>";
                    var newS = "<null>";
                    if ((_parentTransform != null) && (_parentTransform._owningObject is BaseUnit oldParentUnit))
                    {
                        oldS = oldParentUnit.Name;
                        if (oldS == string.Empty)
                            oldS = oldParentUnit.ToString();
                        oldS += " (" + oldParentUnit.ObjId +")";
                    }
                    if ((parent != null) && (parent._owningObject is BaseUnit newParentUnit))
                    {
                        newS = newParentUnit.Name;
                        if (newS == string.Empty)
                            newS = newParentUnit.ToString();
                        newS += " (" + newParentUnit.ObjId +")";
                    }

                    if (_parentTransform?._owningObject != parent?._owningObject)
                        player.SendMessage("|cFF88FF88Changing parent {0} => {1}|r", oldS, newS);
                }
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
                // TODO: This needs better handling and take into account rotations
                child.Local.Position -= Local.Position;
            }
        }

        private void InternalDetachChild(Transform child)
        {
            if (_children.Contains(child))
            {
                _children.Remove(child);
                // TODO: This needs better handling and take into account rotations
                child.Local.Position += Local.Position;
            }
        }

        /// <summary>
        /// Calculates and returns a Transform by processing all underlying parents
        /// </summary>
        /// <returns></returns>
        private PositionAndRotation GetWorldPosition()
        {
            if (_parentTransform == null)
                return _localPosRot;
            var res = _parentTransform.GetWorldPosition().Clone();

            // TODO: This is not taking into account parent rotation !
            res.Translate(Local.Position);
            res.Rotate(Local.Rotation);
            // Is this even correct ?
           
            res.IsLocal = false;
            return res;
        }

        /// <summary>
        /// Detaches the transform, and sets the Local Position and Rotation to what is defined in the WorldSpawnPosition
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
            Local.Rotation = new Vector3(wsp.Roll, wsp.Pitch, wsp.Yaw);
        }

        /// <summary>
        /// Delegates the current position and rotation to the owning GameObject.SetPosition() function
        /// </summary>
        public void FinalizeTransform(bool includeChildren = true)
        {
            _lastFinalizePos = World.ClonePosition();
            if (_owningObject == null)
                return;
            if (!_owningObject.DisabledSetPosition)
                WorldManager.Instance.AddVisibleObject(_owningObject);

            if (includeChildren)
                foreach (var child in _children)
                    child.FinalizeTransform();
            //var rpy = Local.ToRollPitchYaw();
            //_owningObject.SetPosition(Local.Position.X,Local.Position.Y,Local.Position.Z,rpy.X,rpy.Y,rpy.Z);
        }
        
        /// <summary>
        /// Returns a summary of the current local location and parent objects if this is a child
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var res = Local.ToString();
            if (_parentTransform != null)
            {
                res += " on ( ";
                if (_parentTransform._owningObject is BaseUnit bu)
                {
                    if (bu.Name != string.Empty)
                        res += bu.Name + " ";
                    res += "#" + bu.ObjId + " ";
                }
                res += _parentTransform.ToString();
                res += " )";
            }
            return res;
        }

    }
}
