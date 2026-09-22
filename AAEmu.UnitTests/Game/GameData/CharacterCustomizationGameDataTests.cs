using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.GameData;

public sealed class CharacterCustomizationGameDataTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute(
            """
            CREATE TABLE customizing_item_assets (item_id INTEGER, category_id INTEGER, model_id INTEGER, two_tone TEXT, use_pallet TEXT);
            CREATE TABLE item_body_parts (item_id INTEGER, slot_type_id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE customizing_item_asset_colors (id INTEGER, category_id INTEGER);
            CREATE TABLE skin_colors (id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE face_normal_maps (id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE face_diffuse_maps (id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE face_eyelash_maps (id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE body_normal_maps (id INTEGER, model_id INTEGER, npc_only TEXT);
            CREATE TABLE body_diffuse_maps (id INTEGER, model_id INTEGER);
            CREATE TABLE face_decal_assets (id INTEGER, category_id INTEGER, model_id INTEGER, movable TEXT, npc_only TEXT);
            CREATE TABLE game_schedule_beautyshops (game_schedule_id INTEGER, is_pcbang TEXT);
            CREATE TABLE tagged_items (tag_id INTEGER, item_id INTEGER);
            CREATE TABLE items (id INTEGER, exp_abs_lifetime INTEGER);
            """);
    }

    [Test]
    public async Task Load_SkipsAnIncompleteVisibleBodyPartRow()
    {
        // Shipped content contains this model-20, slot-24 row with no item_id.
        Execute("INSERT INTO item_body_parts VALUES (NULL, 24, 20, 'f'), (24127, 24, 20, 'f');");
        var data = new CharacterCustomizationGameData();

        data.Load(Connection);

        await Assert.That(data.IsBodyPartItem(20, EquipmentItemSlot.Hair, 24127)).IsTrue();
        await Assert.That(data.IsBodyPartItem(20, EquipmentItemSlot.Hair, 0)).IsFalse();
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
