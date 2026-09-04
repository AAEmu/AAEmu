namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtySaleCommitter(ISpecialtySaleStore store)
{
    public SpecialtySaleCommitResult Commit(
        SpecialtySaleWrite write,
        Action publishCommitted,
        Action discardPrepared)
    {
        SpecialtySaleCommitResult result;
        try
        {
            result = store.Commit(write);
        }
        catch
        {
            discardPrepared();
            throw;
        }

        if (result == SpecialtySaleCommitResult.Committed)
        {
            publishCommitted();
            return result;
        }

        discardPrepared();
        return result;
    }
}
