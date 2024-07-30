using System;

namespace AAEmu.Game.Models.StaticValues;

/// <summary>
/// 0x02 = Is Moving
/// 0x04 = Is Stopping
/// 0x06 = Is Jumping (Stop+Move)
/// 0x08 = Is in Combat?
/// 0x10 = Has ScType and Phase,
/// 0x40 = Is standing on object GcId
/// </summary>
[Flags]
public enum MoveTypeFlags : byte
{
    None = 0x00,
    _flag1 = 0x01,
    Moving = 0x02,
    Stopping = 0x04, // This might be more accurately named, breaking
    InCombat = 0x08, // Set when fighting
    HasScTypeAndPhase = 0x10,
    _flag6 = 0x20,
    StandingOnObject = 0x40,
    _flag8 = 0x80,

    Jumping = Moving | Stopping, // Jumping seems to be the exception here as it combines moving and stopping
}

[Flags]
public enum MoveTypeActorFlags : byte
{
    None = 0x00,
    _flag1 = 0x01,
    _flag2 = 0x02,
    StandingOnSolid = 0x04,
    _flag4 = 0x08,
    Jumping = 0x10, // Jumping seems to be the exception here as it combines moving and stopping
    StandingOnObject = 0x20, // same as moveTypeFlag 0x40
    HangingFromObject = 0x40, // When we are somehow "sticking" to another object, but don't have it as a parent object
    _flag8 = 0x80,
}
