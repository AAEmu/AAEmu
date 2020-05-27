using SlimMessageBus;

namespace AAEmu.Commons.Messages
{
    public class PlayerEnterRequest : IRequestMessage<PlayerEnterResponse>
    {
        public ulong AccountId { get; set; }
        public uint Token { get; set; }
    }
}
