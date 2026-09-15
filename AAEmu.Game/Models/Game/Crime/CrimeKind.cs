namespace AAEmu.Game.Models.Game.Crime;

/// <summary>
/// Crime vocabulary as shipped by the client's <c>enum_crime_effect_kinds</c>
/// (1 none, 2 assault, 3 murder, 4 theft, 5 bot_report, 6 false_bot_report).
/// </summary>
/// <remarks>
/// 0 is this server's unset sentinel: evidence rows that are not a crime ship <c>crime_kind_id 0</c>.
/// </remarks>
public enum CrimeKind : byte
{
    None,
    Unknown1,
    Assault,
    Murder,
    Theft,
    BotReport,
    FalseBotReport
}
