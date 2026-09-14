using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Expeditions.Recruitment;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IExpeditionRecruitmentJoin : IDisposable
{
    void Persist(MySqlConnection connection, MySqlTransaction transaction);
    void Commit();
}

public interface IExpeditionRecruitmentJoinCoordinator
{
    bool TryBegin(Character actor, Character candidate, Expedition expedition, out IExpeditionRecruitmentJoin transition);
    bool TryBegin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition, out IExpeditionRecruitmentJoin transition);
}

public sealed class ExpeditionRecruitmentJoinCoordinator(ExpeditionManager manager) : IExpeditionRecruitmentJoinCoordinator
{
    public bool TryBegin(Character actor, Character candidate, Expedition expedition,
        out IExpeditionRecruitmentJoin transition)
    {
        if (!manager.TryBeginMemberJoin(actor, candidate, expedition, out var inner))
        {
            transition = null;
            return false;
        }
        transition = new OnlineJoin(inner);
        return true;
    }

    public bool TryBegin(Character actor, ExpeditionJoinCandidate candidate, Expedition expedition,
        out IExpeditionRecruitmentJoin transition)
    {
        if (!manager.TryBeginMemberJoin(actor, candidate, expedition, out var inner))
        {
            transition = null;
            return false;
        }
        transition = new OfflineJoin(inner);
        return true;
    }

    private sealed class OnlineJoin(ExpeditionManager.ExpeditionMemberJoin inner) : IExpeditionRecruitmentJoin
    {
        public void Persist(MySqlConnection connection, MySqlTransaction transaction) => inner.Persist(connection, transaction);
        public void Commit() => inner.Commit();
        public void Dispose() => inner.Dispose();
    }

    private sealed class OfflineJoin(ExpeditionManager.OfflineExpeditionMemberJoin inner) : IExpeditionRecruitmentJoin
    {
        public void Persist(MySqlConnection connection, MySqlTransaction transaction) => inner.Persist(connection, transaction);
        public void Commit() => inner.Commit();
        public void Dispose() => inner.Dispose();
    }
}
