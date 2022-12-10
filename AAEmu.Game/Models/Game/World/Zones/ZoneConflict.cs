using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.Tasks.Zones;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.World.Zones
{
    public class ZoneConflict
    {
        private ZoneGroup _owner;

        public ushort ZoneGroupId { get; set; }
        public int[] NumKills { get; }
        public int[] NoKillMin { get; }

        public int ConflictMin { get; set; }
        public int WarMin { get; set; }
        public int PeaceMin { get; set; }

        public uint PeaceProtectedFactionId { get; set; }
        public uint NuiaReturnPointId { get; set; }
        public uint HariharaReturnPointId { get; set; }
        public uint WarTowerDefId { get; set; }
        // TODO 1.2 // public uint PeaceTowerDefId { get; set; }
        public bool Closed { get; set; }

        public ZoneConflictType CurrentZoneState { get; protected set; }
        public DateTime NextStateTime { get; protected set; }
        public uint KillCount { get; protected set; }

        public ZoneConflict(ZoneGroup owner)
        {
            _owner = owner;

            NumKills = new int[5];
            NoKillMin = new int[5];

            CurrentZoneState = ZoneConflictType.Tension;
            NextStateTime = DateTime.MinValue;
            Closed = false;
        }

        /// <summary>
        /// Call this function if a PvP kill happens in a zone
        /// </summary>
        /// <param name="NumberOfKills"></param>
        public void AddZoneKill(uint NumberOfKills = 1)
        {
            // Ignore when in conflict, war or peace
            if (CurrentZoneState >= ZoneConflictType.Conflict)
                return;
            
            // Ignore if this zone doesn't have a kill counter mechanic
            if ((NumKills[0] == 0) && (NumKills[1] == 0) && (NumKills[2] == 0) && (NumKills[3] == 0) && (NumKills[4] == 0))
                return;

            var LastState = CurrentZoneState;
            KillCount += NumberOfKills;

            if ((CurrentZoneState == ZoneConflictType.Tension) && (KillCount > NumKills[0]))
            {
                CurrentZoneState = ZoneConflictType.Danger;
                NextStateTime = DateTime.MinValue;
            }
            if ((CurrentZoneState == ZoneConflictType.Danger) && (KillCount > NumKills[1]))
            {
                CurrentZoneState = ZoneConflictType.Dispute;
                NextStateTime = DateTime.MinValue;
            }
            if ((CurrentZoneState == ZoneConflictType.Dispute) && (KillCount > NumKills[2]))
            {
                CurrentZoneState = ZoneConflictType.Unrest;
                NextStateTime = DateTime.MinValue;
            }
            if ((CurrentZoneState == ZoneConflictType.Unrest) && (KillCount > NumKills[3]))
            {
                CurrentZoneState = ZoneConflictType.Crisis;
                NextStateTime = DateTime.MinValue;
            }
            if ((CurrentZoneState == ZoneConflictType.Crisis) && (KillCount > NumKills[4]))
            {
                CurrentZoneState = ZoneConflictType.Conflict;
                NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
                KillCount = 0;
            }
            if (LastState != CurrentZoneState)
            {
                SendSwitchZoneState();
            }
        }

        public void SetTimerTask()
        {
            if (NextStateTime > DateTime.MinValue)
            {
                var lpConflictStartTask = new ZoneStateChangeTask(this);
                TaskManager.Instance.Schedule(lpConflictStartTask, this.NextStateTime - DateTime.UtcNow);
            }
        }

        public void SendSwitchZoneState()
        {
            //broadcast to all online clients in server
            WorldManager.Instance.BroadcastPacketToServer(new SCConflictZoneStatePacket(ZoneGroupId, CurrentZoneState, NextStateTime));

            SetTimerTask();
        }

        public void CheckTimer()
        {
            if ((NextStateTime > DateTime.MinValue) && (DateTime.UtcNow >= NextStateTime))
                ForceNextState();
        }

        public void SetState(ZoneConflictType ct)
        {
            if (ct == CurrentZoneState)
                return;
            switch (ct)
            {
                case ZoneConflictType.Conflict:
                    KillCount = 0;
                    NextStateTime = DateTime.UtcNow.AddMinutes(ConflictMin);
                    break;
                case ZoneConflictType.War:
                    KillCount = 0;
                    NextStateTime = DateTime.UtcNow.AddMinutes(WarMin);
                    break;
                case ZoneConflictType.Peace:
                    KillCount = 0;
                    NextStateTime = DateTime.UtcNow.AddMinutes(PeaceMin);
                    break;
                default:
                    NextStateTime = DateTime.MinValue;
                    break;
            }
            CurrentZoneState = ct;
            SendSwitchZoneState();
        }

        public void ForceNextState()
        {
            if (CurrentZoneState < ZoneConflictType.Peace)
            {
                if ((CurrentZoneState == ZoneConflictType.War) && (PeaceMin <= 0))
                {
                    SetState(ZoneConflictType.Conflict);
                }
                else
                {
                    SetState(CurrentZoneState + 1);
                }
            }
            else
            if (CurrentZoneState >= ZoneConflictType.Peace)
            {
                // If it doesn't have a killcounter, go directly back to conflict (ocean areas)
                if ((NumKills[0] == 0) && (NumKills[1] == 0) && (NumKills[2] == 0) && (NumKills[3] == 0) && (NumKills[4] == 0))
                    SetState(ZoneConflictType.Conflict);
                else
                    SetState(ZoneConflictType.Tension);
            }
        }
    }
}
