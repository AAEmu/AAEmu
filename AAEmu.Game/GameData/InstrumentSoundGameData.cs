using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// <c>instrument_sounds</c>: which sources in the shipped content are instruments, and what a play
/// through one carries (its midi patch and the buff that source plays with).
/// </summary>
/// <remarks>
/// The table's <c>kind_id</c> is resolved through <c>enum_instrument_sound_kinds.name</c> rather
/// than by its number, so the two kinds this server routes — an equipped item and a placed doodad —
/// are named by the content itself (<c>ItemKindKey</c>/<c>DoodadKindKey</c> are those names, used as
/// lookups only). <c>item_id</c> is the item template id for kind <c>item</c> and the doodad
/// template id for kind <c>doodad</c>; the shipped table has rows of both kinds (254 items, 41
/// doodads: the grand piano, the drumset, the pipe organs, the festival variants).
///
/// A row is dropped, loudly, when the content cannot answer for it: a kind the enum does not name,
/// or a <c>buff_id</c> no <c>buffs</c> row carries. Nothing falls back to a literal — a dropped row
/// makes the source look like the non-instrument it then is, and the play path refuses it.
/// </remarks>
[GameData]
public class InstrumentSoundGameData : Singleton<InstrumentSoundGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary><c>enum_instrument_sound_kinds.name</c> of the rows keyed by item template id.</summary>
    public const string ItemKindKey = "item";

    /// <summary><c>enum_instrument_sound_kinds.name</c> of the rows keyed by doodad template id.</summary>
    public const string DoodadKindKey = "doodad";

    /// <summary>One <c>instrument_sounds</c> row: the midi patch a source plays with and the buff it carries.</summary>
    public readonly record struct InstrumentSound(uint SourceId, uint Midi, uint BuffId);

    private Dictionary<uint, InstrumentSound> _items = [];
    private Dictionary<uint, InstrumentSound> _doodads = [];

    /// <summary>Row ids whose kind the enum does not name (or names as neither kind this server routes).</summary>
    public IReadOnlyList<uint> UnknownKindRowIds { get; private set; } = [];

    /// <summary><c>buff_id</c> values no <c>buffs</c> row carries; the rows carrying them are dropped.</summary>
    public IReadOnlyList<uint> UnknownBuffIds { get; private set; } = [];

    /// <summary>The kind-name lookup this load resolved, exposed so a load can be asserted as complete.</summary>
    public IReadOnlyCollection<string> KindKeys { get; private set; } = [];

    public void Load(SqliteConnection connection)
    {
        _items = [];
        _doodads = [];
        UnknownKindRowIds = [];
        UnknownBuffIds = [];
        KindKeys = [];

        // A missing enum table is a schema mismatch: it throws, and GameDataManager lets a loader
        // failure fail startup rather than start a server that cannot tell an instrument from a
        // piece of scenery.
        var kindNames = new Dictionary<uint, string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, name FROM enum_instrument_sound_kinds";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var name = reader.GetString("name", string.Empty);
                if (!string.IsNullOrEmpty(name))
                    kindNames[reader.GetUInt32("id")] = name;
            }
        }

        KindKeys = kindNames.Values.Distinct(StringComparer.Ordinal).ToList();

        var buffIds = new HashSet<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM buffs";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                buffIds.Add(reader.GetUInt32("id"));
        }

        var unknownKindRowIds = new List<uint>();
        var unknownBuffIds = new List<uint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, item_id, midi, kind_id, buff_id FROM instrument_sounds";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var rowId = reader.GetUInt32("id");
                var buffId = reader.IsDBNull("buff_id") ? 0u : reader.GetUInt32("buff_id", 0);

                if (!kindNames.TryGetValue(reader.GetUInt32("kind_id"), out var kindName) ||
                    kindName is not (ItemKindKey or DoodadKindKey))
                {
                    unknownKindRowIds.Add(rowId);
                    continue;
                }

                if (buffId != 0 && !buffIds.Contains(buffId))
                {
                    unknownBuffIds.Add(buffId);
                    continue;
                }

                var sound = new InstrumentSound(reader.GetUInt32("item_id"), reader.GetUInt32("midi", 0), buffId);
                if (kindName == ItemKindKey)
                    _items[sound.SourceId] = sound;
                else
                    _doodads[sound.SourceId] = sound;
            }
        }

        UnknownKindRowIds = unknownKindRowIds;
        UnknownBuffIds = unknownBuffIds.Distinct().ToList();

        if (UnknownKindRowIds.Count > 0 || UnknownBuffIds.Count > 0)
            Logger.Error(InstrumentSoundLoadRules.Warning(UnknownKindRowIds, UnknownBuffIds));

        Logger.Info("Loaded {0} instrument items and {1} instrument doodads", _items.Count, _doodads.Count);
    }

    public void PostLoad()
    {
    }

    /// <summary>The equipped-item instrument this template id is, if content says it is one.</summary>
    public bool TryGetItem(uint itemTemplateId, out InstrumentSound sound) =>
        _items.TryGetValue(itemTemplateId, out sound);

    /// <summary>The placed-doodad instrument this template id is, if content says it is one.</summary>
    public bool TryGetDoodad(uint doodadTemplateId, out InstrumentSound sound) =>
        _doodads.TryGetValue(doodadTemplateId, out sound);
}
