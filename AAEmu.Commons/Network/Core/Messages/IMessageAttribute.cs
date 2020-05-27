namespace AAEmu.Commons.Network.Core.Messages
{
    public interface IMessageAttribute<TOpcode>
    {
        TOpcode Opcode { get; set; }
        byte Level { get; set; }
    }
}
