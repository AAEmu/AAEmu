using SlimMessageBus;

namespace AAEmu.Commons.Messages
{
    public class PlayerReconnectRequest : IRequestMessage<PlayerReconnectResponse>
    {
        public uint GameServerId;
        public ulong AccountId;
        public uint Token;
    }
}
