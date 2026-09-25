using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.UnitTests.Utils;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public class MateGameDataTests
{
    [Test]
    public async Task Load_ResolvesNpcCatalogKeysAndRejectsMissing()
    {
        using var connection = CreateConnection(includeRecoveryColumns: true);
        var data = new MateGameData();

        data.Load(connection);

        using var relation = connection.CreateCommand();
        relation.CommandText = "SELECT npc_id FROM item_summon_mates WHERE item_id = $item";
        relation.Parameters.AddWithValue("$item", 501u);
        var npcId = Convert.ToUInt32(relation.ExecuteScalar());
        var summonTemplate = new SummonMateTemplate { Id = 501, NpcId = npcId };
        var state = data.GetRecoveryState(summonTemplate);
        await Assert.That(state.MateReviveDelay).IsEqualTo(7);
        await Assert.That(state.MateReviveHpPercent).IsEqualTo(11);
        await Assert.That(state.MateReviveMpPercent).IsEqualTo(13);
        var unrelatedNpcState = data.GetRecoveryState(202);
        await Assert.That(unrelatedNpcState.MateReviveDelay).IsEqualTo(17);
        await Assert.That(unrelatedNpcState.MateReviveHpPercent).IsEqualTo(19);
        await Assert.That(unrelatedNpcState.MateReviveMpPercent).IsEqualTo(23);
        Assert.Throws<KeyNotFoundException>(() => data.GetRecoveryState(303));
    }

    [Test]
    public async Task ResolveSummonMate_UsesRealItemRelationAndNpcTemplate()
    {
        using var connection = CreateConnection(includeRecoveryColumns: true);
        var data = new MateGameData();
        data.Load(connection);

        var itemManager = new ItemManager(null, null, null, null, null, null);
        foreach (var template in ItemManager.LoadSummonMateTemplates(connection))
            itemManager.SetTemplateForTest(template);

        var npcManager = new NpcManager(null, null, null, null, null);
        npcManager.GetAllTemplates()[101] = new NpcTemplate
        {
            Id = 101,
            Name = "synthetic-mate-npc",
            Level = 9
        };

        using var itemScope = new SingletonScope<ItemManager>(itemManager);
        using var npcScope = new SingletonScope<NpcManager>(npcManager);
        using var dataScope = new SingletonScope<MateGameData>(data);
        var owner = new Character(new UnitCustomModelParams()) { Id = 77 };
        var resolved = new CharacterMates(owner).ResolveSummonMate(501);

        await Assert.That(resolved.ItemTemplate.NpcId).IsEqualTo(101u);
        await Assert.That(resolved.NpcTemplate.Id).IsEqualTo(101u);
        await Assert.That(resolved.RecoveryState.MateReviveDelay).IsEqualTo(7);
        await Assert.That(resolved.RecoveryState.MateReviveHpPercent).IsEqualTo(11);
        await Assert.That(resolved.RecoveryState.MateReviveMpPercent).IsEqualTo(13);
    }

    [Test]
    public async Task Load_MissingRecoveryColumns_FailsLoudly()
    {
        using var connection = CreateConnection(includeRecoveryColumns: false);
        var data = new MateGameData();

        Assert.Throws<SqliteException>(() => data.Load(connection));
        await Assert.That(data).IsNotNull();
    }

    private static SqliteConnection CreateConnection(bool includeRecoveryColumns)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = includeRecoveryColumns
            ? """
              CREATE TABLE npc_mount_skills (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL, mount_skill_id INTEGER NOT NULL);
              CREATE TABLE mount_skills (id INTEGER PRIMARY KEY, name TEXT NOT NULL, skill_id INTEGER NOT NULL);
              CREATE TABLE mount_attached_skills (id INTEGER PRIMARY KEY, mount_skill_id INTEGER NOT NULL, attach_point_id INTEGER NOT NULL, skill_id INTEGER NOT NULL);
              CREATE TABLE mate_equip_slot_packs (id INTEGER PRIMARY KEY, mate_type_id INTEGER NOT NULL, head INTEGER NOT NULL, chest INTEGER NOT NULL, waist INTEGER NOT NULL, feet INTEGER NOT NULL);
              CREATE TABLE mate_equip_pack_groups (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL, mate_equip_pack_id INTEGER NOT NULL);
              CREATE TABLE mate_equip_pack_items (id INTEGER PRIMARY KEY, mate_equip_pack_id INTEGER NOT NULL, item_id INTEGER NOT NULL);
              CREATE TABLE item_summon_mates (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, npc_id INTEGER NOT NULL);
              CREATE TABLE npcs (
                  id INTEGER PRIMARY KEY,
                  mate_revive_delay INTEGER NOT NULL,
                  mate_revive_hp_percent INTEGER NOT NULL,
                  mate_revive_mp_percent INTEGER NOT NULL);
              INSERT INTO npcs(id, mate_revive_delay, mate_revive_hp_percent, mate_revive_mp_percent)
              VALUES (101, 7, 11, 13), (202, 17, 19, 23);
              INSERT INTO item_summon_mates(id, item_id, npc_id) VALUES (1, 501, 101), (2, 502, 303);
              """
            : """
              CREATE TABLE npc_mount_skills (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL, mount_skill_id INTEGER NOT NULL);
              CREATE TABLE mount_skills (id INTEGER PRIMARY KEY, name TEXT NOT NULL, skill_id INTEGER NOT NULL);
              CREATE TABLE mount_attached_skills (id INTEGER PRIMARY KEY, mount_skill_id INTEGER NOT NULL, attach_point_id INTEGER NOT NULL, skill_id INTEGER NOT NULL);
              CREATE TABLE mate_equip_slot_packs (id INTEGER PRIMARY KEY, mate_type_id INTEGER NOT NULL, head INTEGER NOT NULL, chest INTEGER NOT NULL, waist INTEGER NOT NULL, feet INTEGER NOT NULL);
              CREATE TABLE mate_equip_pack_groups (id INTEGER PRIMARY KEY, npc_id INTEGER NOT NULL, mate_equip_pack_id INTEGER NOT NULL);
              CREATE TABLE mate_equip_pack_items (id INTEGER PRIMARY KEY, mate_equip_pack_id INTEGER NOT NULL, item_id INTEGER NOT NULL);
              CREATE TABLE item_summon_mates (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, npc_id INTEGER NOT NULL);
              CREATE TABLE npcs (id INTEGER PRIMARY KEY);
              """;
        command.ExecuteNonQuery();
        return connection;
    }
}
