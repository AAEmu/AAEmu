using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Tasks
{
    public class LeaveWorldTask : Task
    {
        private readonly GameConnection _connection;
        private readonly byte _target;

        public LeaveWorldTask(GameConnection connection, byte target)
        {
            _connection = connection;
            _target = target;
        }

        public override void Execute()
        {
            if (_connection.ActiveChar != null)
            {
                _connection.ActiveChar.DisabledSetPosition = true;
                _connection.ActiveChar.IsOnline = false;
                _connection.ActiveChar.LeaveTime = DateTime.UtcNow;

                // Despawn and unmount everybody from owned Mates
                MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(_connection.ActiveChar);
                
                // Check if still mounted on somebody else's mount and dismount that if needed
                _connection.ActiveChar.ForceDismount(AttachUnitReason.PrefabChanged); // Dismounting a mount because of unsummoning sends "10" for this

                // Remove from Team (raid/party)
                TeamManager.Instance.MemberRemoveFromTeam(_connection.ActiveChar, _connection.ActiveChar, Game.Team.RiskyAction.Leave);

                // Remove from all Chat
                ChatManager.Instance.LeaveAllChannels(_connection.ActiveChar);

                // Handle Family
                if (_connection.ActiveChar.Family > 0)
                    FamilyManager.Instance.OnCharacterLogout(_connection.ActiveChar);

                // Handle Guild
                _connection.ActiveChar.Expedition?.OnCharacterLogout(_connection.ActiveChar);

                // Remove player from world (hides and release Id)
                _connection.ActiveChar.Delete();
                // ObjectIdManager.Instance.ReleaseId(_connection.ActiveChar.ObjId);

                // Cancel auto-regen
                _connection.ActiveChar.StopRegen();

                // Clear Buyback table
                _connection.ActiveChar.BuyBackItems.Wipe();

                // Remove subscribers
                foreach (var subscriber in _connection.ActiveChar.Subscribers)
                    subscriber.Dispose();
            }

            _connection.SaveAndRemoveFromWorld();
            _connection.State = GameState.Lobby;
            _connection.LeaveTask = null;
            _connection.SendPacket(new SCLeaveWorldGrantedPacket(_target));
            _connection.SendPacket(new ChangeStatePacket(0));
        }
    }
}
