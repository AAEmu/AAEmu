using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class BeautyshopEditRulesTests
{
    /// <summary>Nuian male is model 10 (characters.model_id); the ids below are shaped like the content rows.</summary>
    private const uint Model = 10;
    private const uint OtherModel = 11;

    private sealed class Catalog : ICharacterCustomizationCatalog
    {
        public Dictionary<(uint, uint), CustomizingBodyPart> BodyParts { get; } = [];
        public HashSet<(uint, EquipmentItemSlot, uint)> BodyPartItems { get; } = [];
        public HashSet<(CustomizingCategory, uint)> Colors { get; } = [];
        public HashSet<(uint, uint)> SkinColors { get; } = [];
        public HashSet<(uint, uint)> FaceNormalMaps { get; } = [];
        public HashSet<(uint, uint)> BodyNormalMaps { get; } = [];
        public HashSet<(uint, uint)> BodyDiffuseMaps { get; } = [];
        public Dictionary<uint, FaceDecalAsset> Decals { get; } = [];

        public bool TryGetBodyPart(uint modelId, uint itemId, out CustomizingBodyPart part) =>
            BodyParts.TryGetValue((modelId, itemId), out part);
        public bool IsBodyPartItem(uint modelId, EquipmentItemSlot slot, uint itemId) => BodyPartItems.Contains((modelId, slot, itemId));
        public bool IsColor(CustomizingCategory category, uint colorId) => Colors.Contains((category, colorId));
        public bool IsSkinColor(uint modelId, uint id) => SkinColors.Contains((modelId, id));
        public bool IsFaceNormalMap(uint modelId, uint id) => FaceNormalMaps.Contains((modelId, id));
        public bool IsFaceDiffuseMap(uint modelId, uint id) => false;
        public bool IsFaceEyelashMap(uint modelId, uint id) => false;
        public bool IsBodyNormalMap(uint modelId, uint id) => BodyNormalMaps.Contains((modelId, id));
        public bool IsBodyDiffuseMap(uint modelId, uint id) => BodyDiffuseMaps.Contains((modelId, id));
        public bool TryGetFaceDecal(uint id, out FaceDecalAsset decal) => Decals.TryGetValue(id, out decal);
    }

    private static Catalog NuianCatalog()
    {
        var catalog = new Catalog();
        catalog.BodyParts[(Model, 24127)] = new CustomizingBodyPart(Model, CustomizingCategory.Hair, false, false);
        catalog.BodyParts[(Model, 24128)] = new CustomizingBodyPart(Model, CustomizingCategory.Hair, true, true);
        catalog.BodyParts[(24, 40001)] = new CustomizingBodyPart(24, CustomizingCategory.Horn, false, false);
        catalog.Colors.Add((CustomizingCategory.Hair, 3));
        catalog.Colors.Add((CustomizingCategory.Horn, 12600));
        catalog.SkinColors.Add((Model, 1));
        catalog.SkinColors.Add((Model, 37));
        catalog.SkinColors.Add((OtherModel, 2));
        catalog.FaceNormalMaps.Add((Model, 36));
        catalog.BodyNormalMaps.Add((Model, 5));
        catalog.BodyDiffuseMaps.Add((Model, 1));
        catalog.Decals[1714] = new FaceDecalAsset(Model, 1, true, false);   // movable scar
        catalog.Decals[726] = new FaceDecalAsset(Model, 3, false, false);   // makeup, fixed slot 1
        catalog.Decals[564] = new FaceDecalAsset(Model, 4, false, false);   // eyebrow, fixed slot 2
        catalog.Decals[1852] = new FaceDecalAsset(Model, 6, false, false);  // pupil, fixed slots 4 and 5
        catalog.Decals[1177] = new FaceDecalAsset(Model, 3, true, false);   // movable makeup, still fixed slot 1
        catalog.Decals[9001] = new FaceDecalAsset(Model, 3, false, true);   // npc only
        catalog.Decals[9002] = new FaceDecalAsset(OtherModel, 3, false, false);
        return catalog;
    }

    /// <summary>A block a stock client could send for the model above: every id resolves.</summary>
    private static UnitCustomModelParams ValidModel()
    {
        var model = new UnitCustomModelParams(UnitCustomModelType.Face)
        {
            Race = 1,
            Gender = 1,
            HairColor = 3,
            HornColor = 0,
            HairColorId = 4288857148,
            TwoToneHairColor = 4293055420,
            TwoToneFirstWidth = 0.25f,
            TwoToneSecondWidth = 0.68f,
            SkinColorId = 37,
            BodyNormalMap = 5,
            BodyDiffuseMap = 0,
            BodyWeight = 1f
        };
        model.Face.NormalMapId = 36;
        model.Face.NormalMapWeight = 0.27f;
        model.Face.MovableDecalAssetId = 1714;
        model.Face.MovableDecalWeight = 1f;
        model.Face.MovableDecalScale = 1f;
        model.Face.SetFixedDecalAsset(1, 726, 0.12f);
        model.Face.SetFixedDecalAsset(2, 564, 1f);
        model.Face.SetFixedDecalAsset(4, 1852, 1f);
        model.Face.SetFixedDecalAsset(5, 1852, 1f);
        model.Face.Modifier = new byte[128];
        return model;
    }

    private static BeautyshopEditError Validate(UnitCustomModelParams model) =>
        BeautyshopEditRules.ValidateModel(NuianCatalog(), Model, 1, 1, model);

    [Test]
    public async Task IsNone_ZeroAndMinusOneAreTheClientSentinel()
    {
        await Assert.That(BeautyshopEditRules.IsNone(0)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsNone(-1)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsNone(0u)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsNone(uint.MaxValue)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsNone(24127)).IsFalse();
        await Assert.That(BeautyshopEditRules.IsNone(1u)).IsFalse();
    }

    [Test]
    public async Task ValidateBodyPart_NoneLeavesTheSlotAlone()
    {
        var catalog = NuianCatalog();
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(catalog, Model, CustomizingCategory.Hair, 0))
            .IsEqualTo(BeautyshopEditError.None);
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(catalog, Model, CustomizingCategory.Horn, -1))
            .IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateBodyPart_HairOfTheOwnModelPasses()
    {
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(NuianCatalog(), Model, CustomizingCategory.Hair, 24127))
            .IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateBodyPart_HairOfAnotherModelIsUnknown()
    {
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(NuianCatalog(), OtherModel, CustomizingCategory.Hair, 24127))
            .IsEqualTo(BeautyshopEditError.UnknownBodyPart);
    }

    [Test]
    public async Task ValidateBodyPart_HornItemInTheHairSlotIsACategoryMismatch()
    {
        // A Warborn horn (category 2) presented as hair.
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(NuianCatalog(), 24, CustomizingCategory.Hair, 40001))
            .IsEqualTo(BeautyshopEditError.BodyPartCategoryMismatch);
    }

    [Test]
    public async Task ValidateBodyPart_HornOnAModelWithoutHornsIsUnknown()
    {
        // Content has no category 2 rows for model 10, so a Nuian cannot take horns at all.
        await Assert.That(BeautyshopEditRules.ValidateBodyPart(NuianCatalog(), Model, CustomizingCategory.Horn, 40001))
            .IsEqualTo(BeautyshopEditError.UnknownBodyPart);
    }

    [Test]
    public async Task ValidateModel_AStockBlockPasses()
    {
        await Assert.That(Validate(ValidModel())).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_RaceOrGenderMustMatchTheCharacter()
    {
        var model = ValidModel();
        model.Gender = 2;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.RaceOrGenderMismatch);

        model = ValidModel();
        model.Race = 4;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.RaceOrGenderMismatch);
    }

    [Test]
    public async Task ValidateModel_HairColorIdMustBeAHairColorRow()
    {
        var model = ValidModel();
        model.HairColor = 12600; // a horn color row
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownHairColor);

        model.HairColor = uint.MaxValue; // the palette case: none id, RGBA in defaultHairColor
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_HornColorIdMustBeAHornColorRow()
    {
        var model = ValidModel();
        model.HornColor = 3; // a hair color row
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownHornColor);

        model.HornColor = 12600;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_RawRgbaColorsAreFree()
    {
        var model = ValidModel();
        model.HairColorId = 0xDEADBEEF;
        model.TwoToneHairColor = 0x12345678;
        model.Face.LipColor = 1;
        model.Face.LeftPupilColor = 2;
        model.Face.RightPupilColor = 3;
        model.Face.EyebrowColor = 4;
        model.Face.DecoColor = 5;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_SkinColorIsPerModelAndRequired()
    {
        var model = ValidModel();
        model.SkinColorId = 2; // exists, but for model 11
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownSkinColor);

        model.SkinColorId = 0; // no "none" skin: every preset carries one
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownSkinColor);
    }

    [Test]
    public async Task ValidateModel_BodyMapsMayBeZeroOrAnOwnModelRow()
    {
        var model = ValidModel();
        model.BodyNormalMap = 0;
        model.BodyDiffuseMap = 1;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);

        model.BodyNormalMap = 6;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownBodyMap);

        model.BodyNormalMap = 5;
        model.BodyDiffuseMap = 2;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownBodyMap);
    }

    [Test]
    public async Task ValidateModel_FaceMapsMayBeZeroOrAnOwnModelRow()
    {
        var model = ValidModel();
        model.Face.NormalMapId = 0;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);

        model.Face.NormalMapId = 37;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFaceMap);

        // face_diffuse_maps and face_eyelash_maps are empty tables: only 0 passes.
        model.Face.NormalMapId = 36;
        model.Face.DiffuseMapId = 1;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFaceMap);

        model.Face.DiffuseMapId = 0;
        model.Face.EyelashMapId = 1;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFaceMap);
    }

    [Test]
    public async Task ValidateModel_MovableDecalMustBeMovableAndOfTheModel()
    {
        var model = ValidModel();
        model.Face.MovableDecalAssetId = 726; // fixed asset in the movable slot
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownMovableDecal);

        model.Face.MovableDecalAssetId = 0;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_FixedDecalSlotsAreBoundToTheirCategory()
    {
        var model = ValidModel();
        model.Face.SetFixedDecalAsset(1, 564, 1f); // eyebrow in the makeup slot
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFixedDecal);

        model = ValidModel();
        model.Face.SetFixedDecalAsset(1, 1177, 1f); // movable makeup in the makeup slot, as the presets do
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);

        model = ValidModel();
        model.Face.SetFixedDecalAsset(1, 9001, 1f); // npc only
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFixedDecal);

        model = ValidModel();
        model.Face.SetFixedDecalAsset(1, 9002, 1f); // another model
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.UnknownFixedDecal);

        model = ValidModel();
        model.Face.SetFixedDecalAsset(0, 0, 1f);
        model.Face.SetFixedDecalAsset(3, uint.MaxValue, 1f);
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_FloatsMustBeFinite()
    {
        var model = ValidModel();
        model.BodyWeight = float.NaN;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.ValueNotFinite);

        model = ValidModel();
        model.TwoToneFirstWidth = float.PositiveInfinity;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.ValueNotFinite);

        model = ValidModel();
        model.Face.SetFixedDecalAsset(2, 564, float.NegativeInfinity);
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.ValueNotFinite);

        model = ValidModel();
        model.Face.MovableDecalRotate = float.NaN;
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.ValueNotFinite);
    }

    [Test]
    public async Task ValidateModel_ModifierIsCappedAtTheClientBuffer()
    {
        var model = ValidModel();
        model.Face.Modifier = new byte[129];
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.ModifierTooLong);

        model.Face.Modifier = new byte[16];
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task ValidateModel_HairOnlyBlockSkipsTheFaceChecks()
    {
        // ext below Face carries no face block; the T1/T2 fields are still checked.
        var model = new UnitCustomModelParams(UnitCustomModelType.Skin) { Race = 1, Gender = 1, SkinColorId = 1, BodyWeight = 1f };
        await Assert.That(Validate(model)).IsEqualTo(BeautyshopEditError.None);
    }

    [Test]
    public async Task MergeModel_KeepsTheStoredIdentityAndRaceChangeFields()
    {
        var current = new UnitCustomModelParams(UnitCustomModelType.Face)
        {
            Race = 1, Gender = 2, VisualRace = 4, VisualGender = 1, VisualRaceExpiredTime = 123456789L, ModelId = 11, SkinColorId = 1
        };
        var requested = ValidModel();
        requested.Race = 8;
        requested.Gender = 1;
        requested.VisualRace = 6;
        requested.VisualGender = 2;
        requested.VisualRaceExpiredTime = 5;
        requested.ModelId = 99;

        var merged = BeautyshopEditRules.MergeModel(current, requested);

        await Assert.That(merged.Race).IsEqualTo((byte)1);
        await Assert.That(merged.Gender).IsEqualTo((byte)2);
        await Assert.That(merged.VisualRace).IsEqualTo((byte)4);
        await Assert.That(merged.VisualGender).IsEqualTo((byte)1);
        await Assert.That(merged.VisualRaceExpiredTime).IsEqualTo(123456789L);
        await Assert.That(merged.ModelId).IsEqualTo(11u);
        await Assert.That(merged.SkinColorId).IsEqualTo(37u);
        await Assert.That(merged.Face.NormalMapId).IsEqualTo(36u);
        // A fresh object: the request stays untouched and the stored block is not aliased.
        await Assert.That(ReferenceEquals(merged, requested)).IsFalse();
        await Assert.That(ReferenceEquals(merged, current)).IsFalse();
        await Assert.That(requested.Race).IsEqualTo((byte)8);
    }

    [Test]
    public async Task IsValidTicket_TaggedUnexpiredTicketPays()
    {
        var tickets = new HashSet<uint> { 50445, 50446, 50447, 50448, 50449 };
        var now = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);

        await Assert.That(BeautyshopEditRules.IsValidTicket(50447, now.AddDays(3), now, tickets)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsValidTicket(50447, DateTime.MinValue, now, tickets)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsValidTicket(50447, now, now, tickets)).IsFalse();
        await Assert.That(BeautyshopEditRules.IsValidTicket(50447, now.AddMinutes(-1), now, tickets)).IsFalse();
        // 30811 is the packed conversion item, not a pass.
        await Assert.That(BeautyshopEditRules.IsValidTicket(30811, DateTime.MinValue, now, tickets)).IsFalse();
    }

    [Test]
    public async Task IsFreeWindowOpen_NeedsARunningScheduleOfTheAccountKind()
    {
        // game_schedule_beautyshops rows 9 and 10 both point at schedule 750, one pcbang and one not.
        var rows = new List<(int, bool)> { (750, true), (750, false), (616, true) };

        await Assert.That(BeautyshopEditRules.IsFreeWindowOpen(rows, new HashSet<int>(), false)).IsFalse();
        await Assert.That(BeautyshopEditRules.IsFreeWindowOpen(rows, new HashSet<int> { 750 }, false)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsFreeWindowOpen(rows, new HashSet<int> { 616 }, false)).IsFalse();
        await Assert.That(BeautyshopEditRules.IsFreeWindowOpen(rows, new HashSet<int> { 616 }, true)).IsTrue();
        await Assert.That(BeautyshopEditRules.IsFreeWindowOpen([], new HashSet<int> { 750 }, true)).IsFalse();
    }

    [Test]
    public async Task DecideCharge_TicketFirstThenFreeWindowThenRejected()
    {
        await Assert.That(BeautyshopEditRules.DecideCharge(true, false)).IsEqualTo(BeautyshopCharge.Ticket);
        await Assert.That(BeautyshopEditRules.DecideCharge(true, true)).IsEqualTo(BeautyshopCharge.Ticket);
        await Assert.That(BeautyshopEditRules.DecideCharge(false, true)).IsEqualTo(BeautyshopCharge.FreeWindow);
        await Assert.That(BeautyshopEditRules.DecideCharge(false, false)).IsEqualTo(BeautyshopCharge.Rejected);
    }

    [Test]
    public async Task CanEnter_DeadOrEscortedOrAlreadyInsideIsRefused()
    {
        await Assert.That(BeautyshopEditRules.CanEnter(false, false, false)).IsEqualTo(BeautyshopEnterError.None);
        await Assert.That(BeautyshopEditRules.CanEnter(true, false, false)).IsEqualTo(BeautyshopEnterError.Dead);
        await Assert.That(BeautyshopEditRules.CanEnter(false, true, false)).IsEqualTo(BeautyshopEnterError.UnderEscort);
        await Assert.That(BeautyshopEditRules.CanEnter(false, false, true)).IsEqualTo(BeautyshopEnterError.AlreadyInside);
        await Assert.That(BeautyshopEditRules.CanEnter(true, true, true)).IsEqualTo(BeautyshopEnterError.Dead);
    }

    [Test]
    public async Task FixedDecalCategories_CoverTheSixSlots()
    {
        await Assert.That(BeautyshopEditRules.FixedDecalCategories.Length).IsEqualTo(6);
        await Assert.That(BeautyshopEditRules.FixedDecalCategories[4]).IsEqualTo(BeautyshopEditRules.FixedDecalCategories[5]);
    }
}
