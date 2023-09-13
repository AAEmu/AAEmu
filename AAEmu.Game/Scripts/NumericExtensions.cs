using System;

// This needs it's own copy of the extension for the scripts compiler
namespace AAEmu.Game.Scripts
{
    public static class NumericExtensions
    {
        public static double DegToRad(this double val)
        {
            return (Math.PI / 180f) * val;
        }

        public static float DegToRad(this float val)
        {
            return (MathF.PI / 180f) * val;
        }

        public static double RadToDeg(this double val)
        {
            return val / Math.PI * 180f;
        }

        public static float RadToDeg(this float val)
        {
            return val / MathF.PI * 180f;
        }
    }
}
