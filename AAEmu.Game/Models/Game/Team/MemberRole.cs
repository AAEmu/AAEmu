namespace AAEmu.Game.Models.Game.Team;

public enum MemberRole
{
    Undecided = 0,
    Tank = 1,
    Healer = 2,
    Attacker = 3,
    // TMROLE_RANGED_DEALER: the client registers TMROLE_NONE..TMROLE_RANGED_DEALER as the Lua globals
    // 0, 1, 2, 3, 4 (x2game-dev.dll FUN_399f6490, constants DAT_3a074088..DAT_39ff1d10), and the raid
    // recruit role picker offers all five (raid_role_ranged_dealer, ui_texts 12029).
    RangedAttacker = 4
}
