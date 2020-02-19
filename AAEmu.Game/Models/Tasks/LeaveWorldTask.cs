using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.Proxy;

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
                _connection.ActiveChar.LeaveTime = DateTime.Now;

                var activeMate = MateManager.Instance.GetActiveMate(_connection.ActiveChar.ObjId);
                if (activeMate != null)
                {
                    _connection.ActiveChar.Mates.DespawnMate(activeMate.TlId);
                }
                else
                {
                    var isMounted = MateManager.Instance.GetIsMounted(_connection.ActiveChar.ObjId);
                    if (isMounted != null)
                    {
                        if (isMounted.Att2 == _connection.ActiveChar.ObjId)
                        {
                            MateManager.Instance.UnMountMate(_connection.ActiveChar, isMounted.TlId, 2, 5); // TODO - REASON leave world
                        }
                        else
                        {
                            _connection.ActiveChar.Mates.DespawnMate(isMounted.TlId);
                        }
                    }
                }


                if (_connection.ActiveChar.Family > 0)
                    FamilyManager.Instance.OnCharacterLogout(_connection.ActiveChar);
                
                _connection.ActiveChar.Expedition?.OnCharacterLogout(_connection.ActiveChar);

                _connection.ActiveChar.Delete();
                ObjectIdManager.Instance.ReleaseId(_connection.ActiveChar.ObjId);

                _connection.ActiveChar.StopRegen();

                foreach (var item in _connection.ActiveChar.BuyBack)
                    if (item != null)
                        ItemIdManager.Instance.ReleaseId((uint)item.Id);
                Array.Clear(_connection.ActiveChar.BuyBack, 0, _connection.ActiveChar.BuyBack.Length);

                foreach (var subscriber in _connection.ActiveChar.Subscribers)
                    subscriber.Dispose();
            }

            _connection.Save();
            _connection.State = GameState.Lobby;
            _connection.LeaveTask = null;
            _connection.SendPacket(new SCLeaveWorldGrantedPacket(_target));
            _connection.SendPacket(new ChangeStatePacket(0));
        }
    }
}
