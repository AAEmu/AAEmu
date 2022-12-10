using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

// INFO
// https://www.versluis.com/2020/09/what-is-yaw-pitch-and-roll-in-3d-axis-values/
// https://en.wikipedia.org/wiki/Euler_angles
// https://gamemath.com/
// https://en.wikipedia.org/wiki/Conversion_between_quaternions_and_Euler_angles

namespace AAEmu.Game.Models.Game.World.Transform
{

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
        private Transform _stickyParentTransform;
        private List<Transform> _stickyChildren;
        private Vector3 _lastFinalizePos; // Might use this later for cheat detection or delta movement
        public List<Character> _debugTrackers;
        private object _lock = new object();

        /// <summary>
        /// Parent Transform this Transform is attached to, leave null for World
        /// </summary>
        public Transform Parent { get => _parentTransform; set => SetParent(value); }
        /// <summary>
        /// List of Child Transforms of this Transform
        /// </summary>
        public List<Transform> Children { get => _children; }
        public Transform StickyParent { get => _stickyParentTransform; set => SetStickyParent(value); }
        /// <summary>
        /// List of Transforms that are linked to this object, but aren't direct children.
        /// Objects in this list need their positions updated when this object's local transform changes.
        /// Used for ladders on ships for example, only updates children if FinalizeTransform() is called
        /// FinalizeTransform takes the delta from previous call to calculate the delta movement
        /// </summary>
        public List<Transform> StickyChildren { get => _stickyChildren; }
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

        private void InternalInitializeTransform(GameObject owningObject, Transform parentTransform, Transform stickyParentTransform)
        {
            _owningObject = owningObject;
            _parentTransform = parentTransform;
            _stickyParentTransform = stickyParentTransform;
            _children = new List<Transform>();
            _localPosRot = new PositionAndRotation();
            _stickyParentTransform = null;
            _stickyChildren = new List<Transform>();
            _lastFinalizePos = Vector3.Zero;
            _debugTrackers = new List<Character>();
        }

        public Transform(GameObject owningObject, Transform parentTransform = null, Transform stickyParentTransform = null)
        {
            InternalInitializeTransform(owningObject, parentTransform, stickyParentTransform);
        }

        public Transform(GameObject owningObject, Transform parentTransform, float x, float y, float z)
        {
            InternalInitializeTransform(owningObject, parentTransform, null);
            Local.Position = new Vector3(x, y, z);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position)
        {
            InternalInitializeTransform(owningObject, parentTransform, null);
            Local.Position = position;
        }

        public Transform(GameObject owningObject, Transform parentTransform, float posX, float posY, float posZ, float roll, float pitch, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform, null);
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(roll, pitch, yaw);
        }

        public Transform(GameObject owningObject, Transform parentTransform, Vector3 position, Vector3 rotation)
        {
            InternalInitializeTransform(owningObject, parentTransform, null);
            Local.Position = position;
            Local.Rotation = rotation;
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float roll, float pitch, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform,null);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(roll, pitch, yaw);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, float posX, float posY, float posZ, float yaw)
        {
            InternalInitializeTransform(owningObject, parentTransform,null);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            Local.Position = new Vector3(posX, posY, posZ);
            Local.Rotation = new Vector3(0f, 0f, yaw);
        }

        public Transform(GameObject owningObject, Transform parentTransform, uint worldId, uint zoneId, uint instanceId, PositionAndRotation posRot)
        {
            InternalInitializeTransform(owningObject, parentTransform,null);
            WorldId = worldId;
            ZoneId = zoneId;
            InstanceId = instanceId;
            _localPosRot = new PositionAndRotation(posRot.Position, posRot.Rotation);
        }

        /// <summary>
        /// Clones a Transform including GameObject and Parent Transform information, does not include stickyParent
        /// </summary>
        /// <returns></returns>
        public Transform Clone()
        {
            return new Transform(_owningObject, _parentTransform, WorldId, ZoneId, InstanceId, _localPosRot);
        }
        
        /// <summary>
        /// Clones a Transform, keeps the parent Transform set, but replaces owning object with newOwner, does not include stickyParent
        /// </summary>
        /// <param name="newOwner"></param>
        /// <returns></returns>
        public Transform Clone(GameObject newOwner)
        {
            return new Transform(newOwner, _parentTransform, WorldId, ZoneId, InstanceId, _localPosRot);
        }

        /// <summary>
        /// Clones a Transform without GameObject or Parent Transform, using the current World relative position, does not include stickyParent
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
        /// Clones a Transform using childObject as new owner and setting Parent Transform to the current transform,
        /// the new clone has a local position initialized as 0,0,0
        /// does not include stickyParent
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
        /// Detaches this Transform from it's Parent, and detaches all it's children.
        /// Children get their World Transform as Local
        /// </summary>
        public void DetachAll(bool keepStickyParent = false)
        {
            Parent = null;
            for (var i = Children.Count - 1; i >= 0; i--)
                Children[i].Parent = null;
            if (!keepStickyParent)
                for (var i = _stickyChildren.Count - 1; i >= 0; i--)
                    _stickyChildren[i].StickyParent = null;
        }

        /// <summary>
        /// Assigns a new Parent Transform, automatically handles related child Transforms
        /// </summary>
        /// <param name="parent"></param>
        protected void SetParent(Transform parent)
        {
            if (_parentTransform == parent) return;
            lock (_lock)
            {

                if ((parent == null) || (!parent.Equals(_parentTransform)))
                {
                    if (_parentTransform != null)
                        _parentTransform.InternalDetachChild(this);
                    /*
                    if (_owningObject != null)
                    {
                        var oldS = "<null>";
                        var newS = "<null>";
                        if ((_parentTransform != null) && (_parentTransform._owningObject is BaseUnit oldParentUnit))
                        {
                            oldS = oldParentUnit.Name;
                            if (oldS == string.Empty)
                                oldS = oldParentUnit.ToString();
                            oldS += " (" + oldParentUnit.ObjId + ")";
                        }

                        if ((parent != null) && (parent._owningObject is BaseUnit newParentUnit))
                        {
                            newS = newParentUnit.Name;
                            if (newS == string.Empty)
                                newS = newParentUnit.ToString();
                            newS += " (" + newParentUnit.ObjId + ")";
                        }

                        if ((_owningObject is Character player) &&
                            (_parentTransform?._owningObject != parent?._owningObject))
                            player.SendMessage($"|cFF88FF88Changing parent - {oldS} => {newS}|r");
                        // Console.WriteLine("Transform {0} - Changing parent - {1} => {2}", GameObject?.ObjId.ToString() ?? "<null>", oldS, newS);
                    }
                    */

                    _parentTransform = parent;
                    _parentTransform?.InternalAttachChild(this);

                    if ((_owningObject is Character aPlayer))
                        aPlayer.SendMessage($"NewPos: {ToFullString(true, true)}");
                }
            }
        }

        private void InternalAttachChild(Transform child)
        {
            if (!_children.Contains(child))
            {
                _children.Add(child);
                // TODO: This needs better handling and take into account rotations
                //child.Local.SubDistance(World.Position);
                child.Local.Position -= World.Position;
                child.Local.Rotation -= World.Rotation;
                child.GameObject.ParentObj = this.GameObject;
            }
        }

        private void InternalDetachChild(Transform child)
        {
            if (_children.Contains(child))
            {
                _children.Remove(child);
                // TODO: This needs better handling and take into account rotations
                child.Local.Rotation += World.Rotation;
                child.Local.Position += World.Position;
                //child.Local.AddDistance(World.Position);
                child.GameObject.ParentObj = null;
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
        public void ApplyWorldSpawnPosition(WorldSpawnPosition wsp,uint newInstanceId = 0,bool keepStickyParent = false)
        {
            DetachAll(keepStickyParent);
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
            var worldPosDelta = World.ClonePosition() - _lastFinalizePos;
            if (worldPosDelta == Vector3.Zero)
            {
                return;
            }
            else
            {
                foreach (var character in _debugTrackers)
                {
                    /*
                    character.SendMessage("{0} - Delta: ({2}  {3}  {4}) - {1}",
                        _owningObject.ObjId,
                        (_owningObject is BaseUnit bu) ? bu.Name : "<gameobject>",
                        worldPosDelta.X.ToString("F1"),worldPosDelta.Y.ToString("F1"),worldPosDelta.Z.ToString("F1"));
                    */
                    /*
                    character.SendMessage("["+DateTime.UtcNow.ToString("HH:mm:ss") + "] {0} - ZoneKey: {2} Region: ({3} {4}) - {1}",
                        _owningObject.ObjId,
                        (_owningObject is BaseUnit bu) ? bu.Name : "<gameobject>",
                        ZoneId, _owningObject?.Region?.X.ToString() ?? "??", _owningObject?.Region?.Y.ToString() ?? "??");
                    */
                }
            }
            
            // TODO: Check if/make sure rotations are taken into account
            if (_stickyChildren.Count > 0)
            {
                for(var i = _stickyChildren.Count-1; i >= 0; i--)
                {
                    var stickyChild = _stickyChildren[i];
                    if (stickyChild == null)
                        continue;
                    stickyChild.Local.Translate(worldPosDelta);
                    stickyChild.FinalizeTransform(includeChildren);
                    WorldManager.Instance.AddVisibleObject(stickyChild._owningObject);                        

                    if (!(stickyChild.GameObject is Unit))
                        continue;
                    
                    
                    // Create a moveType
                    /*
                    var mt = new UnitMoveType();
                    var wPos = stickyChild.World.Clone();
                    //mt.Flags = 0x00;
                    mt.Flags = 0x40; // sticky/attached
                    mt.X = wPos.Position.X;
                    mt.Y = wPos.Position.Y;
                    mt.Z = wPos.Position.Z;
                    var (r, p, y) = wPos.ToRollPitchYawSBytesMovement();
                    mt.DeltaMovement[1] = 127;
                    mt.RotationX = r;
                    mt.RotationY = p;
                    mt.RotationZ = y;
                    mt.ActorFlags = 0x40; // sticky/climbing
                    mt.ClimbData = 1; // ladder is sticky ?
                    mt.Stance = 6;
                    
                    // Related to object we're sticking to
                    // First 13 bits is for vertical offset (Z)
                    // Next 8 bits is for horizontal offset (Y?)
                    // upper 8 bits is 0x7F when sticking to a vine or ladder, this might possibly be the depth (X?)
                    mt.GcId = 0; 
                    stickyChild.GameObject.BroadcastPacket(
                        new SCOneUnitMovementPacket(stickyChild.GameObject.ObjId, mt),
                        false);
                    */
                }
            }
           
            if (_owningObject == null)
                return;
            
            if (!_owningObject.DisabledSetPosition)
                WorldManager.Instance.AddVisibleObject(_owningObject);

            if (_owningObject is Slave slave)
            {
                foreach (var dood in slave.AttachedDoodads)
                    WorldManager.Instance.AddVisibleObject(dood);
                foreach (var chld in slave.AttachedSlaves)
                    WorldManager.Instance.AddVisibleObject(chld);
            }
            /*
            if (_owningObject is Transfer transfer)
            {
                foreach (var dood in transfer.AttachedDoodads)
                    WorldManager.Instance.AddVisibleObject(dood);
                foreach (var chr in transfer?.AttachedCharacters)
                    if (chr != null)
                    {
                        chr.Transform.StickyParent = transfer.Transform;
                        WorldManager.Instance.AddVisibleObject(chr);
                    }
            }
            */

            if (includeChildren)
            {
                for (int i = _children.Count - 1; i >= 0; i--)
                {
                    var child = _children[i];
                    child?.FinalizeTransform(includeChildren);
                }
            }
            
            ResetFinalizeTransform();            
            _owningObject.SetPosition(Local.Position.X,Local.Position.Y,Local.Position.Z,Local.Rotation.X,Local.Rotation.Y,Local.Rotation.Z);
        }

        public void ResetFinalizeTransform()
        {
            _lastFinalizePos = World.ClonePosition();
        }

        /// <summary>
        /// Returns a summary of the current local location and parent objects if this is a child
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return ToFullString(true, false);
        }
        public string ToFullString(bool isFirstInList = true,bool chatFormatted = false)
        {
            var chatColorWhite = chatFormatted ? "|cFFFFFFFF" : "";
            var chatColorGreen = chatFormatted ? "|cFF00FF00" : "";
            var chatColorYellow = chatFormatted ? "|cFFFFFF00" : "";
            var chatColorRestore = chatFormatted ? "|r" : "";
            var chatLineFeed = chatFormatted ? "\n" : "";
            var res = string.Empty;
            if (isFirstInList && ((_parentTransform != null) || (_stickyParentTransform != null)))
                res += "[" + chatColorWhite + World.ToString() + chatColorRestore + "] " + chatLineFeed + "=> "; 
            res += Local.ToString();
            if (_parentTransform != null)
            {
                res += "\n on ( ";
                if (_parentTransform._owningObject is BaseUnit bu)
                {
                    if (bu.Name != string.Empty)
                        res += chatColorGreen + bu.Name + chatColorRestore + " ";
                    res += "#" + chatColorWhite + bu.ObjId + chatColorRestore + " ";
                }

                res += _parentTransform.ToFullString(false, chatFormatted);
                res += " )" + chatLineFeed;
            }

            if (_stickyParentTransform != null)
            {
                res += "\n=> sticking to ( ";
                if (_stickyParentTransform._owningObject is BaseUnit bu)
                {
                    if (bu.Name != string.Empty)
                        res += chatColorYellow + bu.Name + chatColorRestore + " ";
                    res += "#" + chatColorWhite + bu.ObjId + chatColorRestore + " ";
                }

                res += _stickyParentTransform.ToFullString(false, chatFormatted);
                res += " )" + chatLineFeed;
            }
            return res;
        }

        /// <summary>
        /// Add child to StickyChildren list, these children are not included in parent/child relations, but are updated with delta movements
        /// </summary>
        /// <param name="stickyChild"></param>
        /// <returns>Returns true if successfully attached, or false if already attached or other errors</returns>
        public bool AttachStickyTransform(Transform stickyChild)
        {
            // Null-check
            if ((stickyChild == null) || (stickyChild.GameObject == null))
                return false;
            // Check if already there
            if (StickyChildren.Contains(stickyChild))
                return false;
            // Check if in the same world
            if ((stickyChild.WorldId != this.WorldId) || (stickyChild.InstanceId != this.InstanceId))
                return false;
            StickyChildren.Add(stickyChild);
            stickyChild._stickyParentTransform = this;
            return true;
        }

        /// <summary>
        /// Detaches child from StickyChildren list, and sets the child's stickyParent to null
        /// </summary>
        /// <param name="stickyChild"></param>
        public void DetachStickyTransform(Transform stickyChild)
        {
            if (StickyChildren.Contains(stickyChild))
                _stickyChildren.Remove(stickyChild);
            stickyChild._stickyParentTransform = null;
        }

        protected void SetStickyParent(Transform stickyParent)
        {
            if (_stickyParentTransform == stickyParent) return;

            Parent = null; // detach from parent if on any
            
            lock (_lock)
            {

                // var oldStickyParent = _stickyParentTransform;
                // Detach from previous sticky parent if needed 
                if ((_stickyParentTransform != null) && (!_stickyParentTransform.Equals(stickyParent)))
                    _stickyParentTransform.DetachStickyTransform(this);

                /*
                if (oldStickyParent != stickyParent)
                {
                    var oldS = "<null>";
                    var newS = "<null>";
                    if ((oldStickyParent != null) && (oldStickyParent._owningObject is BaseUnit oldParentUnit))
                    {
                        oldS = oldParentUnit.Name;
                        if (oldS == string.Empty)
                            oldS = oldParentUnit.ToString();
                        oldS += " (" + oldParentUnit.ObjId + ")";
                    }

                    if ((stickyParent != null) && (stickyParent._owningObject is BaseUnit newParentUnit))
                    {
                        newS = newParentUnit.Name;
                        if (newS == string.Empty)
                            newS = newParentUnit.ToString();
                        newS += " (" + newParentUnit.ObjId + ")";
                    }

                    if (GameObject is Character player)
                        player.SendMessage($"|cFFFF88FFChanging Sticky - {oldS} => {newS}|r");
                    //Console.WriteLine("Transform {0} - Changing Sticky - {1} => {2}", GameObject?.ObjId.ToString() ?? "<null>", oldS, newS);
                }
                */
                
                // Attach to new parent if needed
                if (stickyParent != null)
                    stickyParent.AttachStickyTransform(this);
            }
            
            // Attach to Stick target's parent if it has one
            Parent = stickyParent?.Parent;
        }

        public bool ToggleDebugTracker(Character player)
        {
            if (_debugTrackers.Contains(player))
            {
                _debugTrackers.Remove(player);
                return false;
            }
            else
            {
                _debugTrackers.Add(player);
                return true;
            }
        }

    }
}
