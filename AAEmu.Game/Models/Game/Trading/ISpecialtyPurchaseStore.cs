namespace AAEmu.Game.Models.Game.Trading;

public interface ISpecialtyPurchaseStore
{
    bool Commit(SpecialtyPurchaseWrite write);
}
