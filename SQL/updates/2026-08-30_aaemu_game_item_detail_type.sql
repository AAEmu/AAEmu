-- Preserve the discriminator required to decode each persisted item detail body.
ALTER TABLE `items`
ADD COLUMN `detail_type` TINYINT UNSIGNED NOT NULL DEFAULT '0' AFTER `count`;

-- Existing detail blobs predate the persisted discriminator. Dedicated item classes identify
-- their format directly; base Item owns the remaining formats that share those body lengths.
UPDATE `items`
SET `detail_type` = CASE
    WHEN `type` IN (
        'AAEmu.Game.Models.Game.Items.EquipItem',
        'AAEmu.Game.Models.Game.Items.Armor',
        'AAEmu.Game.Models.Game.Items.Weapon',
        'AAEmu.Game.Models.Game.Items.Accessory'
    ) THEN 1
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.SummonSlave' THEN 2
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.SummonMate' THEN 3
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.UccItem' THEN 4
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.TreasureMap' THEN 5
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.BigFish' THEN 6
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.MusicSheetItem' THEN 8
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.Backpack'
        AND OCTET_LENGTH(`details`) = 10 THEN 12
    WHEN `type` = 'AAEmu.Game.Models.Game.Items.Item' THEN CASE OCTET_LENGTH(`details`)
        WHEN 33 THEN 2
        WHEN 20 THEN 3
        WHEN 9 THEN 4
        WHEN 24 THEN 11
        WHEN 16 THEN 7
        WHEN 8 THEN 14
        WHEN 4 THEN 9
        WHEN 12 THEN 10
        WHEN 10 THEN 12
        WHEN 13 THEN 13
        ELSE 0
    END
    ELSE 0
END
WHERE `detail_type` = 0
  AND `details` IS NOT NULL
  AND OCTET_LENGTH(`details`) > 0;
