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
        public Point Position { get; set; }
        public Point WorldPosition { get; set; }
        public Region Region { get; set; }
        public DateTime Respawn { get; set; }
        public DateTime Despawn { get; set; }
        public virtual bool IsVisible { get; set; }
        public GameObject ParentObj { get; set; }

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

            var charMoved = false;
            var lastZoneKey = Position.ZoneId;
            if (this is Character)
            {
                if (!Position.X.Equals(x) || !Position.Y.Equals(y) || !Position.Z.Equals(z))
                    charMoved = true;
            }

            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Position.RotationX = rotationX;
            Position.RotationY = rotationY;
            Position.RotationZ = rotationZ;
            WorldManager.Instance.AddVisibleObject(this);

            if ((charMoved) && (this is Character))
            {
                var thisChar = ((Character)this);
                TeamManager.Instance.UpdatePosition(thisChar.Id);
                // ZoneId was updated in AddVisibleObject, so we can check against it here
                if (Position.ZoneId != lastZoneKey)
                {
                    // We switched zonekeys, we need to do some checks
                    var lastZone = ZoneManager.Instance.GetZoneByKey(lastZoneKey);
                    var newZone = ZoneManager.Instance.GetZoneByKey(Position.ZoneId);
                    var lastZoneGroupId = (short)((lastZone != null)?lastZone.GroupId : 0);
                    var newZoneGroupId = (short)((newZone != null)?newZone.GroupId : 0);
                    if (lastZoneGroupId != newZoneGroupId)
                    {


                        // Ok, we actually changed zone groups, we'll leave to do some chat channel stuff
                        if (lastZoneGroupId != 0)
                        {
                            ChatManager.Instance.GetZoneChat(lastZoneKey).LeaveChannel(thisChar);
                            //thisChar.SendPacket(new SCLeavedChatChannelPacket(ChatType.Shout, lastZoneGroupId, 0));
                        }

                        if (newZoneGroupId != 0)
                        {
                            ChatManager.Instance.GetZoneChat(Position.ZoneId).JoinChannel(thisChar);
                            //thisChar.SendPacket(new SCJoinedChatChannelPacket(ChatType.Shout, newZoneGroupId, 0));
                        }

                        if ((newZone == null) || (newZone.Closed))
                        {
                            // Entered a forbidden zone
                            /*
                            if (!thisChar.isGM)
                            {
                                // TODO: for non-GMs, add a timed task to kick them out (recall to last Nui)
                                // TODO: Remove backpack immediatly
                            }
                            */
                            // Send extra info to player if we are still in a real but unreleased zone (not null), this is not retail behaviour
                            if (newZone != null)
                                thisChar.SendMessage(ChatType.System, "|cFFFF0000You have entered a closed zone ({0} - {1})!\nPlease leave immediatly!|r",newZone.ZoneKey,newZone.Name);
                            // Send the error message
                            thisChar.SendErrorMessage(ErrorMessageType.ClosedZone);
                        }
                    }
                }
            }
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
