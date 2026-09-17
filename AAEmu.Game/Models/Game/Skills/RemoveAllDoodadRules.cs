using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a remove_all_doodad (special type 140) row is allowed to delete.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 52 rows. <c>value1</c> is the radius in millimetres — the same unit
/// <c>remove_doodad</c> (58) and <c>remove_doodad_group</c> (130) carry in their <c>value2</c>, where those
/// rows fill <c>value1</c> with their template or group id — 100,000 mm on 34 rows and 2,000 to 170,000 on
/// the rest. <c>value2</c> is 1 on 50 rows and 0 on two (25435, and the test skill 두대드 제거 50349 at
/// 5,000 mm) and no shipped row establishes what it means, so it is not read. The rows that reach it are
/// siege effects that clear a barrier: 고전포 발사 15235 ("철벽의 결계를 무력화 시킵니다" — neutralises the
/// wall of steel) and 무모한 돌진 21538 ("무모한 돌진 발동시 주변의 철벽의 결계를 해제합니다" — clears the
/// surrounding wall of steel), plus four buff ticks and the test skill.
/// </remarks>
public static class RemoveAllDoodadRules
{
    /// <summary>
    /// Whether a doodad in range may be deleted. Housing doodads are excluded: a house or its furniture is
    /// not one of the barriers the rows describe, nothing in the content names one, and <c>Doodad.Delete</c>
    /// on a persistent housing doodad removes it from the database — destroying what a player owns with no
    /// way back. This is a safety choice, not something a row states.
    /// </summary>
    public static bool ShouldRemove(DoodadOwnerType ownerType) => ownerType != DoodadOwnerType.Housing;
}
