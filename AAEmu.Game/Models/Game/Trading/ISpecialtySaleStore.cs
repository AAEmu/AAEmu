namespace AAEmu.Game.Models.Game.Trading;

public enum SpecialtySaleCommitResult
{
    Committed,
    PackNotPersisted,
    LaborConflict
}

public interface ISpecialtySaleStore
{
    SpecialtySaleCommitResult Commit(SpecialtySaleWrite write);
}
