using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Mails;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// Compact <c>content_configs</c> rows Bless / Arche Pass / mail require. Production loads them;
/// tests that call <see cref="ContentConfigGameData.RequireInt"/> must seed first.
/// </summary>
public static class ContentConfigTestSeed
{
    public static void BlessAndArchePass()
    {
        var data = ContentConfigGameData.Instance;
        data.SetForTest(BlessUthstinRules.ConfigBaseStats, 200);
        data.SetForTest(BlessUthstinRules.ConfigMaxStatsLimit, 300);
        data.SetForTest(BlessUthstinRules.ConfigExtendPerPoint, 20);
        data.SetForTest(BlessUthstinRules.ConfigApplyLimit, 1);
        data.SetForTest(BlessUthstinRules.ConfigInitItem, 47084);
        data.SetForTest(BlessUthstinRules.ConfigExtendItem, 47084);
        data.SetForTest(BlessUthstinRules.ConfigInitItemCount, 1);
        data.SetForTest(BlessUthstinRules.ConfigSelectCost, 600);
        data.SetForTest(BlessUthstinRules.ConfigCopyCost, 500);
        data.SetForTest(BlessUthstinRules.ConfigExpandItem, 39559);
        data.SetForTest(BlessUthstinRules.ConfigExpandPage2, 1);
        data.SetForTest(BlessUthstinRules.ConfigExpandPage3, 2);
        data.SetForTest(ArchePassRules.ConfigMissionCompleteCount, 20);
        data.SetForTest(ArchePassRules.ConfigMissionChangeCount, 6);
        data.SetForTest(ArchePassRules.ConfigResetWeeklyDay, 1);
    }

    /// <summary>The mail charge rows and the normal-delivery delay.</summary>
    public static void Mail()
    {
        var data = ContentConfigGameData.Instance;
        data.SetForTest(MailFeeRules.NormalMailCostKey, 50);
        data.SetForTest(MailFeeRules.ExpressMailCostKey, 500);
        data.SetForTest(MailFeeRules.NormalAttachmentCostKey, 30);
        data.SetForTest(MailFeeRules.ExpressAttachmentCostKey, 400);
        data.SetForTest(MailFeeRules.AttachmentDelayByTargetKey, 1800);
    }
}
