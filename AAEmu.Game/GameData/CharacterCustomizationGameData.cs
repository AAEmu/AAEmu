using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// The customization tables the beauty shop and the lobby character edit validate against, keyed the
/// way the client keys them: by the character's model (characters.model_id, 12 creatable models).
/// </summary>
[GameData]
public class CharacterCustomizationGameData : Singleton<CharacterCustomizationGameData>, IGameDataLoader, ICharacterCustomizationCatalog
{
    /// <summary>
    /// tags.id 4990 '뷰티샵- 미용 이용권' (beauty shop: beauty ticket). It tags the five period passes
    /// 50445 to 50449 and the retired test item 51915, which carries no lifetime; only the passes are
    /// loaded below.
    /// </summary>
    public const uint BeautyTicketTagId = 4990;

    private Dictionary<(uint ModelId, uint ItemId), CustomizingBodyPart> _bodyParts = [];
    private HashSet<(uint ModelId, EquipmentItemSlot Slot, uint ItemId)> _bodyPartItems = [];
    private HashSet<(CustomizingCategory Category, uint Id)> _colors = [];
    private HashSet<(uint ModelId, uint Id)> _skinColors = [];
    private HashSet<(uint ModelId, uint Id)> _faceNormalMaps = [];
    private HashSet<(uint ModelId, uint Id)> _faceDiffuseMaps = [];
    private HashSet<(uint ModelId, uint Id)> _faceEyelashMaps = [];
    private HashSet<(uint ModelId, uint Id)> _bodyNormalMaps = [];
    private HashSet<(uint ModelId, uint Id)> _bodyDiffuseMaps = [];
    private Dictionary<uint, FaceDecalAsset> _faceDecals = [];
    private List<(int ScheduleId, bool PcBang)> _beautyshopSchedules = [];
    private HashSet<uint> _ticketItemIds = [];

    public IReadOnlyList<(int ScheduleId, bool PcBang)> BeautyshopSchedules => _beautyshopSchedules;
    public IReadOnlySet<uint> TicketItemIds => _ticketItemIds;

    public void Load(SqliteConnection connection)
    {
        _bodyParts = [];
        _bodyPartItems = [];
        _colors = [];
        _skinColors = [];
        _faceNormalMaps = [];
        _faceDiffuseMaps = [];
        _faceEyelashMaps = [];
        _bodyNormalMaps = [];
        _bodyDiffuseMaps = [];
        _faceDecals = [];
        _beautyshopSchedules = [];
        _ticketItemIds = [];

        // 478 rows: the hair (category 1), horn (2) and tail (3) items each model may wear. two_tone and
        // use_pallet are the flags the client reads back before sending.
        ReadRows(connection, "SELECT item_id, category_id, model_id, two_tone, use_pallet FROM customizing_item_assets", reader =>
        {
            var modelId = reader.GetUInt32("model_id");
            var itemId = reader.GetUInt32("item_id");
            _bodyParts[(modelId, itemId)] = new CustomizingBodyPart(
                modelId,
                (CustomizingCategory)reader.GetInt32("category_id", 0),
                reader.GetBoolean("two_tone"),
                reader.GetBoolean("use_pallet"));
        });

        // 763 rows: every body part per model and slot. slot_type_id 23..29 (enum_equip_slot_types face,
        // hair, glasses, horns, tail, body, beard) maps onto equipment slots 19..25 (enum_equip_slot).
        ReadRows(connection, "SELECT item_id, slot_type_id, model_id, npc_only FROM item_body_parts", reader =>
        {
            if (reader.GetBoolean("npc_only"))
                return;
            var slotTypeId = reader.GetInt32("slot_type_id", 0);
            if (slotTypeId < 23 || slotTypeId > 29)
                return;
            // compact.sqlite3 includes incomplete rows (for example, a visible model/slot row with no item).
            // They cannot describe an equippable body part, so keep them out of the validation index.
            if (reader.IsDBNull("model_id") || reader.IsDBNull("item_id"))
                return;
            _bodyPartItems.Add((reader.GetUInt32("model_id"), (EquipmentItemSlot)(slotTypeId - 4), reader.GetUInt32("item_id")));
        });

        // 12740 rows, category 1 (hair) or 2 (horn). Not keyed by model: 34 of the 146 shipped PC presets
        // reference a color row of another model.
        ReadRows(connection, "SELECT id, category_id FROM customizing_item_asset_colors", reader =>
            _colors.Add(((CustomizingCategory)reader.GetInt32("category_id", 0), reader.GetUInt32("id"))));

        ReadRows(connection, "SELECT id, model_id, npc_only FROM skin_colors", reader =>
        {
            if (!reader.GetBoolean("npc_only"))
                _skinColors.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id")));
        });
        ReadRows(connection, "SELECT id, model_id, npc_only FROM face_normal_maps", reader =>
        {
            if (!reader.GetBoolean("npc_only"))
                _faceNormalMaps.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id")));
        });
        // Both ship empty in 10.0.2.13; read anyway so a later content drop is picked up.
        ReadRows(connection, "SELECT id, model_id, npc_only FROM face_diffuse_maps", reader =>
        {
            if (!reader.GetBoolean("npc_only"))
                _faceDiffuseMaps.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id")));
        });
        ReadRows(connection, "SELECT id, model_id, npc_only FROM face_eyelash_maps", reader =>
        {
            if (!reader.GetBoolean("npc_only"))
                _faceEyelashMaps.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id")));
        });
        ReadRows(connection, "SELECT id, model_id, npc_only FROM body_normal_maps", reader =>
        {
            if (!reader.GetBoolean("npc_only"))
                _bodyNormalMaps.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id")));
        });
        ReadRows(connection, "SELECT id, model_id FROM body_diffuse_maps", reader =>
            _bodyDiffuseMaps.Add((reader.GetUInt32("model_id"), reader.GetUInt32("id"))));

        // 1787 rows; category is enum_face_decal_category (1 scar.. 6 pupil).
        ReadRows(connection, "SELECT id, category_id, model_id, movable, npc_only FROM face_decal_assets", reader =>
            _faceDecals[reader.GetUInt32("id")] = new FaceDecalAsset(
                reader.GetUInt32("model_id"),
                (byte)reader.GetInt32("category_id", 0),
                reader.GetBoolean("movable"),
                reader.GetBoolean("npc_only")));

        // 7 rows, all pointing at 2018 to 2020 event schedules; whether one runs is GameScheduleManager's call.
        ReadRows(connection, "SELECT game_schedule_id, is_pcbang FROM game_schedule_beautyshops", reader =>
            _beautyshopSchedules.Add((reader.GetInt32("game_schedule_id", 0), reader.GetBoolean("is_pcbang"))));

        // 6 tagged rows. 51915 ('머리 모양 변경권_test') is a retired test item with exp_abs_lifetime 0,
        // so its ExpirationTime never leaves MinValue and it would pass as a ticket that never expires;
        // only the five period passes 50445 to 50449 carry a lifetime.
        ReadRows(connection,
            "SELECT ti.item_id FROM tagged_items ti JOIN items i ON i.id = ti.item_id " +
            "WHERE ti.tag_id = " + BeautyTicketTagId + " AND i.exp_abs_lifetime > 0",
            reader => _ticketItemIds.Add(reader.GetUInt32("item_id")));
    }

    public void PostLoad()
    {
    }

    private static void ReadRows(SqliteConnection connection, string sql, Action<SQLiteWrapperReader> onRow)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            onRow(reader);
    }

    public bool TryGetBodyPart(uint modelId, uint itemId, out CustomizingBodyPart part) => _bodyParts.TryGetValue((modelId, itemId), out part);
    public bool IsBodyPartItem(uint modelId, EquipmentItemSlot slot, uint itemId) => _bodyPartItems.Contains((modelId, slot, itemId));
    public bool IsColor(CustomizingCategory category, uint colorId) => _colors.Contains((category, colorId));
    public bool IsSkinColor(uint modelId, uint id) => _skinColors.Contains((modelId, id));
    public bool IsFaceNormalMap(uint modelId, uint id) => _faceNormalMaps.Contains((modelId, id));
    public bool IsFaceDiffuseMap(uint modelId, uint id) => _faceDiffuseMaps.Contains((modelId, id));
    public bool IsFaceEyelashMap(uint modelId, uint id) => _faceEyelashMaps.Contains((modelId, id));
    public bool IsBodyNormalMap(uint modelId, uint id) => _bodyNormalMaps.Contains((modelId, id));
    public bool IsBodyDiffuseMap(uint modelId, uint id) => _bodyDiffuseMaps.Contains((modelId, id));
    public bool TryGetFaceDecal(uint id, out FaceDecalAsset decal) => _faceDecals.TryGetValue(id, out decal);
}
