namespace AAEmu.Commons.Network.Core.Messages
{
    public interface IMessageHandlerAttribute<TOpcode>
    {
        TOpcode Opcode { get; set; }
    }
}
