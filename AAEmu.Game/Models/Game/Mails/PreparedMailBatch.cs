namespace AAEmu.Game.Models.Game.Mails;

public sealed class PreparedMailBatch
{
    internal PreparedMailBatch(
        IReadOnlyList<BaseMail> mails,
        IReadOnlyList<string> receiverNames,
        IReadOnlyList<uint> generatedMailIds)
    {
        Mails = mails;
        ReceiverNames = receiverNames;
        GeneratedMailIds = generatedMailIds;
    }

    public IReadOnlyList<BaseMail> Mails { get; }
    internal IReadOnlyList<string> ReceiverNames { get; }
    internal IReadOnlyList<uint> GeneratedMailIds { get; }
    internal bool IsCompleted { get; set; }
}
