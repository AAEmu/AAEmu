using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Trading;

public interface ISpecialtyMarketStore
{
    SpecialtyMarketState Load();
    void Commit(SpecialtyMarketWrite write);

    /// <summary>
    /// Applies within the caller's transaction, without committing or publishing state.
    /// The caller must roll back the transaction if this operation throws.
    /// </summary>
    void Apply(MySqlConnection connection, MySqlTransaction transaction, SpecialtyMarketWrite write);
}
