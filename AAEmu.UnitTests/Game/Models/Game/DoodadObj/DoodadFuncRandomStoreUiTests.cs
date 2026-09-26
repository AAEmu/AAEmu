using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Game.Models.Game.Merchant;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public sealed class DoodadFuncRandomStoreUiTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("""
            CREATE TABLE doodad_func_random_store_uis (
                id INTEGER PRIMARY KEY,
                merchant_random_pack_id INTEGER NOT NULL
            );
            CREATE TABLE doodad_func_groups (
                id INTEGER PRIMARY KEY,
                doodad_almighty_id INTEGER NOT NULL
            );
            CREATE TABLE doodad_funcs (
                id INTEGER PRIMARY KEY,
                doodad_func_group_id INTEGER NOT NULL,
                actual_func_id INTEGER NOT NULL,
                actual_func_type TEXT NOT NULL
            );
            """);
    }

    [Test]
    public async Task DescriptorLoader_LoadsTypedRandomStoreDescriptor()
    {
        var manager = CreateDoodadManager();
        Execute("INSERT INTO doodad_func_random_store_uis VALUES (17, 29);");

        manager.LoadRandomStoreUiFunctions(Connection);

        var descriptor = manager.GetFuncTemplate(17, nameof(DoodadFuncRandomStoreUi));
        await Assert.That(descriptor).IsTypeOf<DoodadFuncRandomStoreUi>();
        await Assert.That(((DoodadFuncRandomStoreUi)descriptor).MerchantRandomPackId).IsEqualTo(29u);
    }

    [Test]
    public async Task DescriptorLoader_RejectsZeroRows()
    {
        var manager = CreateDoodadManager();
        Execute("INSERT INTO doodad_func_random_store_uis VALUES (17, 0);");

        await Assert.That(() => manager.LoadRandomStoreUiFunctions(Connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task DoodadPackMapping_LoadsOnceAndRejectsDuplicateAlmightyId()
    {
        Execute("""
            INSERT INTO doodad_func_random_store_uis VALUES (1, 11);
            INSERT INTO doodad_func_groups VALUES (100, 900);
            INSERT INTO doodad_funcs VALUES (1, 100, 1, 'DoodadFuncRandomStoreUi');
            """);

        var mapping = RandomMerchantGameData.LoadDoodadPacks(Connection);

        await Assert.That(mapping[900]).IsEqualTo(11u);

        Execute("""
            INSERT INTO doodad_func_random_store_uis VALUES (2, 12);
            INSERT INTO doodad_func_groups VALUES (101, 900);
            INSERT INTO doodad_funcs VALUES (2, 101, 2, 'DoodadFuncRandomStoreUi');
            """);

        await Assert.That(() => RandomMerchantGameData.LoadDoodadPacks(Connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task UiService_BuildsTheExistingRandomShopInfoPacket()
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 41 };
        var window = new RandomShopWindow
        {
            CharacterId = character.Id,
            PackId = 7,
            FreeUsed = 2,
            ChargeUsed = 1,
            RolledAt = RandomShopTestContent.AnyMoment,
            Offers =
            [
                new RandomShopOffer
                {
                    GroupId = 1,
                    GoodId = 2,
                    Slot = 0,
                    ItemId = 3,
                    Cost = 4,
                    Sold = false
                }
            ]
        };

        var packet = RandomShopUiService.BuildPacket(character, window.PackId, window.PackId, window);

        await Assert.That(packet.Type).IsEqualTo(window.PackId);
        await Assert.That(packet.DisplayType).IsEqualTo(window.PackId);
        await Assert.That(packet.FreeCnt).IsEqualTo((byte)2);
        await Assert.That(packet.ChargeCnt).IsEqualTo((byte)1);
        await Assert.That(packet.Dbid).IsEqualTo(character.Id);
        await Assert.That(packet.DisplayGoods).HasCount(1);
        await Assert.That(packet.DisplayGoods[0].ItemId).IsEqualTo(3u);
    }

    [Test]
    public async Task PublicDescriptor_LeavesWindowToClientRequestAndDoesNotAdvancePhase()
    {
        const uint descriptorId = 17;
        const uint packId = 1;
        const uint templateId = 901;
        const uint characterId = 902;
        const uint doodadObjId = 903;

        var randomShop = RandomMerchantManager.Instance;
        var previousContent = GetPrivateField<IReadOnlyDictionary<uint, uint>>(
            RandomMerchantGameData.Instance, "_packByDoodadTemplate");
        var previousLookup = GetPrivateField<Func<uint, RandomMerchantPack>>(randomShop, "_packLookup");
        var previousStore = GetPrivateField<IRandomShopStateStore>(randomShop, "_store");

        try
        {
            randomShop.UseContent(RandomShopTestContent.Build());
            randomShop.UseStore(new InMemoryRandomShopStore());
            SetPrivateField(
                RandomMerchantGameData.Instance,
                "_packByDoodadTemplate",
                new Dictionary<uint, uint> { [templateId] = packId });

            var world = new WorldInstance(new WorldTemplate { Name = "q10-test" }, 0, true, 12);
            var character = new Character(new UnitCustomModelParams())
            {
                Id = characterId
            };
            SetPrivateField(character, "_parentWorld", world);
            var doodad = new Doodad
            {
                ObjId = doodadObjId,
                TemplateId = templateId,
                IsVisible = true
            };
            SetPrivateField(doodad, "_parentWorld", world);
            SetPrivateField(doodad, "CurrentFuncs", new List<DoodadFunc>
            {
                new()
                {
                    FuncId = descriptorId,
                    FuncType = nameof(DoodadFuncRandomStoreUi),
                    PermId = (uint)DoodadFuncPermission.Public
                }
            });

            var descriptor = new DoodadFuncRandomStoreUi
            {
                Id = descriptorId,
                MerchantRandomPackId = packId
            };
            var windows = GetPrivateField<Dictionary<(uint, uint), RandomShopWindow>>(
                randomShop, "_windows");
            windows.Remove((characterId, packId));
            descriptor.Use(character, doodad, skillId: 0);

            await Assert.That(windows.ContainsKey((characterId, packId))).IsFalse();
            await Assert.That(doodad.ToNextPhase).IsFalse();
        }
        finally
        {
            randomShop.UseContent(null);
            randomShop.UseStore(previousStore);
            SetPrivateField(RandomMerchantGameData.Instance, "_packByDoodadTemplate", previousContent);
            SetPrivateField(randomShop, "_packLookup", previousLookup);
        }
    }

    [Test]
    public async Task NonPublicDescriptor_FailsClosedBeforeOpeningWindow()
    {
        var character = new Character(new UnitCustomModelParams());
        var doodad = new Doodad();
        SetPrivateField(doodad, "CurrentFuncs", new List<DoodadFunc>
        {
            new()
            {
                FuncId = 17,
                FuncType = nameof(DoodadFuncRandomStoreUi),
                PermId = (uint)DoodadFuncPermission.Owner
            }
        });

        var allowed = DoodadFuncRandomStoreUi.TryAuthorize(
            character,
            doodad,
            new DoodadFuncRandomStoreUi { Id = 17, MerchantRandomPackId = 1 });

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task OutOfRangeDescriptorPermission_FailsClosedBeforeEnumCast()
    {
        var character = new Character(new UnitCustomModelParams());
        var doodad = new Doodad();
        SetPrivateField(doodad, "CurrentFuncs", new List<DoodadFunc>
        {
            new()
            {
                FuncId = 17,
                FuncType = nameof(DoodadFuncRandomStoreUi),
                PermId = byte.MaxValue + 1
            }
        });

        var allowed = DoodadFuncRandomStoreUi.TryAuthorize(
            character,
            doodad,
            new DoodadFuncRandomStoreUi { Id = 17, MerchantRandomPackId = 1 });

        await Assert.That(allowed).IsFalse();
    }

    [Test]
    public async Task NpcTypeLevelPackTemplate_IsIncludedByExistingPermanentDoodadPath()
    {
        var manager = CreateDoodadManager();
        var template = new DoodadTemplate
        {
            Id = 1234,
            Model = "npctype://5678"
        };
        SetPrivateField(manager, "_templates", new Dictionary<uint, DoodadTemplate>
        {
            [template.Id] = template
        });

        var wanted = new HashSet<uint>();
        manager.AddNpcTypeTemplateIds(wanted);

        await Assert.That(wanted.Contains(template.Id)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: false,
            npcTypeModel: true,
            talkOrQuestFunc: false,
            towerAlmighty: false,
            ignoredPermanent: false,
            scheduledEvent: false)).IsTrue();
    }

    [Test]
    public async Task RealCompactContent_IsConsistentWhenExplicitlyGated()
    {
        var path = Environment.GetEnvironmentVariable("AAEMU_Q10_CONTENT_DB");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT u.merchant_random_pack_id, f.func_skill_id, f.perm_id,
                   f.next_phase, g.doodad_almighty_id, a.model
            FROM doodad_func_random_store_uis u
            JOIN doodad_funcs f
              ON f.actual_func_id = u.id
             AND f.actual_func_type = 'DoodadFuncRandomStoreUi'
            JOIN doodad_func_groups g ON g.id = f.doodad_func_group_id
            JOIN doodad_almighties a ON a.id = g.doodad_almighty_id
            """;
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(await reader.ReadAsync()).IsTrue();

        var packId = reader.GetInt64(0);
        var skillId = reader.GetInt64(1);
        var permission = reader.GetInt64(2);
        var nextPhase = reader.GetInt64(3);
        var model = reader.GetString(5);

        await Assert.That(await reader.ReadAsync()).IsFalse();
        await Assert.That(packId).IsGreaterThan(0);
        await Assert.That(skillId).IsGreaterThan(0);
        await Assert.That(permission).IsGreaterThanOrEqualTo(0);
        await Assert.That(permission).IsLessThanOrEqualTo((long)Enum.GetValues<DoodadFuncPermission>().Max());
        await Assert.That(nextPhase).IsEqualTo(-1);
        await Assert.That(model.StartsWith("npctype://", StringComparison.Ordinal)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: false,
            npcTypeModel: true,
            talkOrQuestFunc: false,
            towerAlmighty: false,
            ignoredPermanent: false,
            scheduledEvent: false)).IsTrue();

        await reader.CloseAsync();
        command.CommandText = "SELECT COUNT(*) FROM merchant_random_packs WHERE id = $pack";
        command.Parameters.AddWithValue("$pack", packId);
        await Assert.That((long)(await command.ExecuteScalarAsync())!).IsEqualTo(1);
    }

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static DoodadManager CreateDoodadManager()
    {
        var manager = new DoodadManager(
            Mock.Of<INonUnitObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object,
            Mock.Of<IFactionManager>().Object);
        SetPrivateField(
            manager,
            "_funcTemplates",
            new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>());
        return manager;
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return (T)field.GetValue(instance)!;
        }

        throw new MissingFieldException(instance.GetType().FullName, name);
    }

    private static void SetPrivateField(object instance, string name, object value)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }
        }

        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.SetMethod != null)
            {
                property.SetValue(instance, value);
                return;
            }
        }

        throw new MissingFieldException(instance.GetType().FullName, name);
    }
}
