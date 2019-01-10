using System;
using System.Net;
using AAEmu.Commons.Network.Type;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Core.Network.Game
{
    public class GameNetwork : Singleton<GameNetwork>
    {
        private Server _server;
        private GameProtocolHandler _handler;
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private GameNetwork()
        {
            _handler = new GameProtocolHandler();

            // World
            RegisterPacket(0x000, 1, typeof(X2EnterWorldPacket));
            RegisterPacket(0x001, 1, typeof(CSLeaveWorldPacket));
            RegisterPacket(0x002, 1, typeof(CSCancelLeaveWorldPacket));
            RegisterPacket(0x01f, 1, typeof(CSListCharacterPacket));
            RegisterPacket(0x020, 1, typeof(CSRefreshInCharacterListPacket));
            RegisterPacket(0x021, 1, typeof(CSCreateCharacterPacket));
            RegisterPacket(0x022, 1, typeof(CSEditCharacterPacket));
            RegisterPacket(0x023, 1, typeof(CSDeleteCharacterPacket));
            RegisterPacket(0x024, 1, typeof(CSSelectCharacterPacket));
            RegisterPacket(0x025, 1, typeof(CSSpawnCharacterPacket));
            RegisterPacket(0x026, 1, typeof(CSCancelCharacterDeletePacket));
            RegisterPacket(0x027, 1, typeof(CSNotifyInGamePacket));
            RegisterPacket(0x028, 1, typeof(CSNotifyInGameCompletedPacket));
            RegisterPacket(0x02a, 1, typeof(CSChangeTargetPacket));
            RegisterPacket(0x036, 1, typeof(CSDestroyItemPacket));
            RegisterPacket(0x037, 1, typeof(CSSplitBagItemPacket));
            RegisterPacket(0x038, 1, typeof(CSSwapItemsPacket));
            RegisterPacket(0x050, 1, typeof(CSStartSkillPacket));
            RegisterPacket(0x061, 1, typeof(CSSendChatMessagePacket));
            RegisterPacket(0x063, 1, typeof(CSInteractNPCPacket));
            RegisterPacket(0x064, 1, typeof(CSInteractNPCEndPacket));
            RegisterPacket(0x088, 1, typeof(CSMoveUnitPacket));
            RegisterPacket(0x0f6, 1, typeof(CSChangeAppellationPacket));
            RegisterPacket(0x0fb, 1, typeof(CSSetLpManageCharacterPacket));
            RegisterPacket(0x101, 1, typeof(CSAddFriendPacket));
            RegisterPacket(0x102, 1, typeof(CSDeleteFriendPacket));
            RegisterPacket(0x104, 1, typeof(CSAddBlockedUserPacket));
            RegisterPacket(0x105, 1, typeof(CSDeleteBlockedUserPacket));
            RegisterPacket(0x10f, 1, typeof(CSNotifySubZonePacket));
            RegisterPacket(0x113, 1, typeof(CSRequestUIDataPacket));
            RegisterPacket(0x114, 1, typeof(CSSaveUIDataPacket));
            RegisterPacket(0x115, 1, typeof(CSBroadcastVisualOptionPacket));
            RegisterPacket(0x116, 1, typeof(CSRestrictCheckPacket));
            RegisterPacket(0x12e, 1, typeof(CSIdleStatusPacket));

            // Proxy
            RegisterPacket(0x000, 2, typeof(ChangeStatePacket));
            RegisterPacket(0x001, 2, typeof(FinishStatePacket));
            RegisterPacket(0x002, 2, typeof(FlushMsgsPacket));
            RegisterPacket(0x004, 2, typeof(UpdatePhysicsTimePacket));
            RegisterPacket(0x005, 2, typeof(BeginUpdateObjPacket));
            RegisterPacket(0x006, 2, typeof(EndUpdateObjPacket));
            RegisterPacket(0x007, 2, typeof(BeginBindObjPacket));
            RegisterPacket(0x008, 2, typeof(EndBindObjPacket));
            RegisterPacket(0x009, 2, typeof(UnbindPredictedObjPacket));
            RegisterPacket(0x00A, 2, typeof(RemoveStaticObjPacket));
            RegisterPacket(0x00B, 2, typeof(VoiceDataPacket));
            RegisterPacket(0x00C, 2, typeof(UpdateAspectPacket));
            RegisterPacket(0x00D, 2, typeof(SetAspectProfilePacket));
            RegisterPacket(0x00E, 2, typeof(PartialAspectPacket));
            RegisterPacket(0x00F, 2, typeof(SetGameTypePacket));
            RegisterPacket(0x010, 2, typeof(ChangeCVarPacket));
            RegisterPacket(0x011, 2, typeof(EntityClassRegistrationPacket));
            RegisterPacket(0x012, 2, typeof(PingPacket));
            RegisterPacket(0x013, 2, typeof(PongPacket));
            RegisterPacket(0x014, 2, typeof(PacketSeqChange));

            // Proxy Old Version: 0.5
            RegisterPacket(0x015, 2, typeof(FastPingPacket));
            RegisterPacket(0x016, 2, typeof(FastPongPacket));
        }

        public void Start()
        {
            var config = AppConfiguration.Instance.Network;
            _server = new Server(new IPEndPoint(IPAddress.Any, config.Port), 10);
            _server.SetHandler(_handler);
            _server.Start();

            _log.Info("Network started");
        }

        public void Stop()
        {
            if (_server.IsStarted)
                _server.Stop();
            
            _log.Info("Network stoped");
        }
        
        private void RegisterPacket(uint type, byte level, Type classType)
        {
            _handler.RegisterPacket(type, level, classType);
        }
    }
}