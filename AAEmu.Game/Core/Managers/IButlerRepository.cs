using AAEmu.Game.Models.Game.Butlers;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IButlerRepository
{
    IReadOnlyList<CharacterButlerRecord> LoadAll();
    IReadOnlyList<CharacterButlerStateRecord> LoadAllStates() =>
        [.. LoadAll().Select(record => new CharacterButlerStateRecord(
            record, new Dictionary<sbyte, ulong>(), Array.Empty<ButlerHarvestJob>(),
            Array.Empty<ButlerStoredItem>(), Array.Empty<ButlerSpecialtyTradeJob>()))];
    bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId);
    bool TryChangeHouse(CharacterButlerRecord record, uint expectedHouseId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    void Save(CharacterButlerRecord record, MySqlConnection connection, MySqlTransaction transaction);
    void SavePermanentData(uint characterId, sbyte key, ulong value, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    void DeletePermanentData(uint characterId, sbyte key, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    long InsertHarvestJob(uint characterId, ButlerHarvestJobCandidate candidate, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    bool UpdateHarvestJob(uint characterId, ButlerHarvestJob job, ushort expectedRemainingRepeatCount,
        long expectedUpdateTime, MySqlConnection connection, MySqlTransaction transaction) =>
        throw new NotSupportedException();
    bool DeleteHarvestJob(uint characterId, long jobId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    int DeleteAllHarvestJobs(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    int DeleteAllSpecialtyTradeJobs(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    long InsertSpecialtyTradeJob(uint characterId, ButlerSpecialtyTradeJobCandidate candidate,
        MySqlConnection connection, MySqlTransaction transaction) => throw new NotSupportedException();
    bool DeleteSpecialtyTradeJob(uint characterId, long jobId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job) =>
        throw new NotSupportedException();
    bool TryInsertHarvestCompletion(long jobId, ushort cycleNumber, long completedAt,
        MySqlConnection connection, MySqlTransaction transaction) => throw new NotSupportedException();
    void SaveStoredItem(uint characterId, ButlerStoredItem item, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    bool DeleteStoredItem(uint characterId, ulong itemId,
        MySqlConnection connection, MySqlTransaction transaction) => throw new NotSupportedException();
    int DeleteAllStoredItems(uint characterId, MySqlConnection connection,
        MySqlTransaction transaction) => throw new NotSupportedException();
    void Delete(uint characterId);
}
