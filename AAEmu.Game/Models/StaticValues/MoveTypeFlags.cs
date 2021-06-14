using System;

namespace AAEmu.Game.Models.StaticValues
{
    [Flags]
    public enum MoveTypeFlags : byte
    {
        None = 0x00,
        Moving = 0x02,
        Stopping = 0x04, // this might be more accurately named, breaking
        Jumping = 0x06, // Jumping seems to be the exception here as it combines moving and stopping
        StandingOnObject = 0x40
    }

    public enum MoveTypeActorFlags : byte
    {
        None = 0x00,
        StandingOnSolid = 0x04,
        Jumping = 0x10, // Jumping seems to be the exception here as it combines moving and stopping
        StandingOnObject = 0x20, // same as moveTypeFlag 0x40
        HangingFromObject = 0x40 // When we are somehow "sticking" to another object, but don't have it as a parent object
    }
   
}
