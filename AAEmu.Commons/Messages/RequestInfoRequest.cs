using SlimMessageBus;

namespace AAEmu.Commons.Messages
{
    public class RequestInfoRequest : IRequestMessage<RequestInfoResponse>
    {
        public ulong AccountId { get; set; }
        
        #region Overrides of Object
        public override string ToString() => $"RequestInfoRequest(AccountId={AccountId})";
        #endregion
    }
}
