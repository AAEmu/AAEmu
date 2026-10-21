using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillFireAnimRulesTests
{
    // Opaque stand-ins for the pose columns a skill row can carry (default / two-hand / dual / …).
    private const uint DefaultOneHand = 10;
    private const uint TwoHand = 20;
    private const uint DualWield = 30;
    private const uint ShotGun = 40;
    private const uint StringInstrument = 50;
    private const uint PercussionInstrument = 51;
    private const uint TubeInstrument = 52;

    private const uint StringHoldable = 100;
    private const uint PercussionHoldable = 101;
    private const uint TubeHoldable = 102;
    private const uint ShotGunHoldable = 103;

    private static uint Resolve(
        WeaponWieldKind wield,
        uint mainhandPoseId = 0,
        uint rangedHoldableId = 0,
        uint musicalHoldableId = 0,
        uint defaultId = DefaultOneHand,
        uint twoHandId = TwoHand,
        uint dualId = DualWield,
        uint shotGunId = ShotGun,
        uint stringId = StringInstrument,
        uint percussionId = PercussionInstrument,
        uint tubeId = TubeInstrument) =>
        SkillFireAnimRules.ResolveFireAnimId(
            defaultId, twoHandId, dualId, shotGunId, stringId, percussionId, tubeId,
            wield, mainhandPoseId, rangedHoldableId, musicalHoldableId,
            StringHoldable, PercussionHoldable, TubeHoldable, ShotGunHoldable);

    [Test]
    public async Task OneHand_UsesDefaultFireAnim()
    {
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, mainhandPoseId: (uint)AnimPose.OneHand))
            .IsEqualTo(DefaultOneHand);
        await Assert.That(Resolve(WeaponWieldKind.None)).IsEqualTo(DefaultOneHand);
    }

    [Test]
    public async Task TwoHandSwordPose_UsesTwoHandColumn_WhenAuthored()
    {
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.TwoHand))
            .IsEqualTo(TwoHand);
    }

    [Test]
    public async Task StaffPose_KeepsDefault_EvenWhenTwoHandColumnAuthored()
    {
        // Staff is TwoHanded on the equip slot but enum_anim_poses.staff — no staff fire-anim column.
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.Staff))
            .IsEqualTo(DefaultOneHand);
    }

    [Test]
    public async Task DualWield_UsesDualColumn_WhenAuthored()
    {
        await Assert.That(Resolve(WeaponWieldKind.DuelWielded)).IsEqualTo(DualWield);
    }

    [Test]
    public async Task MissingPoseColumn_FallsBackToDefault()
    {
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.TwoHand,
                twoHandId: 0))
            .IsEqualTo(DefaultOneHand);
        await Assert.That(Resolve(WeaponWieldKind.DuelWielded, dualId: 0)).IsEqualTo(DefaultOneHand);
    }

    [Test]
    public async Task Shotgun_OverridesWhenRangedHoldableMatches()
    {
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, rangedHoldableId: ShotGunHoldable))
            .IsEqualTo(ShotGun);
        // No shotgun column authored → keep the default even with a shotgun equipped.
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, rangedHoldableId: ShotGunHoldable, shotGunId: 0))
            .IsEqualTo(DefaultOneHand);
    }

    [Test]
    public async Task Instrument_OverridesFromMusicalSlot_NotMainhand()
    {
        // Instruments equip to EquipmentItemSlot.Musical; a mainhand match must not win.
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, musicalHoldableId: StringHoldable))
            .IsEqualTo(StringInstrument);
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, musicalHoldableId: PercussionHoldable))
            .IsEqualTo(PercussionInstrument);
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, musicalHoldableId: TubeHoldable))
            .IsEqualTo(TubeInstrument);
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.TwoHand,
                musicalHoldableId: 0))
            .IsEqualTo(TwoHand);
    }

    [Test]
    public async Task OnlyDefaultColumn_StaysDefaultForEveryWield()
    {
        // Rows that ship a single fire_anim_id (no pose overrides) must not invent a clip.
        const uint onlyDefault = 99;
        await Assert.That(Resolve(WeaponWieldKind.OneHanded, defaultId: onlyDefault, twoHandId: 0, dualId: 0))
            .IsEqualTo(onlyDefault);
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.TwoHand,
                defaultId: onlyDefault,
                twoHandId: 0,
                dualId: 0))
            .IsEqualTo(onlyDefault);
        await Assert.That(Resolve(
                WeaponWieldKind.TwoHanded,
                mainhandPoseId: (uint)AnimPose.Staff,
                defaultId: onlyDefault,
                twoHandId: 0,
                dualId: 0))
            .IsEqualTo(onlyDefault);
        await Assert.That(Resolve(WeaponWieldKind.DuelWielded, defaultId: onlyDefault, twoHandId: 0, dualId: 0))
            .IsEqualTo(onlyDefault);
    }

    [Test]
    public async Task ArmsUseAnimTimeDelay_SkipsAutoAttackWeaponAnim()
    {
        await Assert.That(SkillFireAnimRules.ArmsUseAnimTimeDelay(useAnimTime: true, isAutoAttackWeaponAnim: true))
            .IsFalse();
        await Assert.That(SkillFireAnimRules.ArmsUseAnimTimeDelay(useAnimTime: true, isAutoAttackWeaponAnim: false))
            .IsTrue();
        await Assert.That(SkillFireAnimRules.ArmsUseAnimTimeDelay(useAnimTime: false, isAutoAttackWeaponAnim: false))
            .IsFalse();
    }
}
