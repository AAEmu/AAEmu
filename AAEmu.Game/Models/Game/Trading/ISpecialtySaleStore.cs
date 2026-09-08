namespace AAEmu.Game.Models.Game.Trading;

public enum SpecialtySaleCommitResult
{
    Committed,
    PackNotPersisted,
    LaborConflict,
    MarketConflict
}

public interface ISpecialtySaleStore
{
    SpecialtySaleCommitResult Commit(SpecialtySaleWrite write);
}
