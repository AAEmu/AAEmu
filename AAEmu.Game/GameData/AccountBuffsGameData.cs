using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Premium;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// account_buffs — the paid memberships an account can hold, and the labor each adds on top of the
/// premium grade. Row ids double as the <c>extraKind</c> of an account attribute.
/// </summary>
/// <remarks>
/// Nothing loaded this table before, so the memberships contributed nothing server-side while the
/// client added them to everything it displayed.
/// </remarks>
[GameData]
public class AccountBuffsGameData : Singleton<AccountBuffsGameData>, IGameDataLoader
{
    private Dictionary<uint, AccountBuff> _buffs = [];

    public AccountBuff Get(uint id) => _buffs.GetValueOrDefault(id);

    /// <summary>
    /// Adds the memberships to a grade's labor numbers exactly the way the client does: the caps always
    /// accumulate, while a rate either accumulates or replaces the grade's own, per the row's
    /// replace_premium_*_lp flag. A replacing membership wins over the grade but still stacks with the
    /// other memberships, which is the only reading that keeps the sum order-independent.
    /// </summary>
    public LaborAllowance Apply(PremiumGrade grade, IEnumerable<uint> membershipIds)
    {
        var allowance = new LaborAllowance
        {
            OnlineRate = Math.Max(0, grade?.OnlineLabor ?? 0),
            OfflineRate = Math.Max(0, grade?.OfflineLabor ?? 0),
            MaxLabor = Math.Max(0, grade?.MaxLabor ?? 0),
            MaxLocalLabor = Math.Max(0, grade?.MaxLocalLabor ?? 0)
        };

        if (membershipIds == null)
            return allowance;

        var replacedOnline = false;
        var replacedOffline = false;

        foreach (var id in membershipIds.Distinct())
        {
            var buff = Get(id);
            if (buff == null)
                continue;

            if (buff.ReplacePremiumOnlineLp && !replacedOnline)
            {
                replacedOnline = true;
                allowance.OnlineRate = 0;
            }

            if (buff.ReplacePremiumOfflineLp && !replacedOffline)
            {
                replacedOffline = true;
                allowance.OfflineRate = 0;
            }

            allowance.OnlineRate += buff.OnlineLaborPower;
            allowance.OfflineRate += buff.OfflineLaborPower;
            allowance.MaxLabor += buff.AddMaxLp;
            allowance.MaxLocalLabor += buff.AddMaxLocalLp;
        }

        return allowance;
    }

    public void Load(SqliteConnection connection)
    {
        _buffs = [];

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM account_buffs";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var buff = new AccountBuff
            {
                Id = reader.GetUInt32("id"),
                Name = reader.GetString("name", string.Empty),
                BuffId = reader.GetUInt32("buff_id", 0),
                OnlineLaborPower = reader.GetInt32("online_laborpower", 0),
                ReplacePremiumOnlineLp = reader.GetBoolean("replace_premium_online_lp", true),
                OfflineLaborPower = reader.GetInt32("offline_laborpower", 0),
                ReplacePremiumOfflineLp = reader.GetBoolean("replace_premium_offline_lp", true),
                AddMaxLp = reader.GetInt32("add_max_lp", 0),
                AddMaxLocalLp = reader.GetInt32("add_max_local_lp", 0)
            };
            _buffs[buff.Id] = buff;
        }
    }

    public void PostLoad()
    {
        // Nothing to do here
    }
}
