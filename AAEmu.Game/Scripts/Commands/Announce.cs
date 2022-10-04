using System;
using System.Drawing;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class Announce : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("announce", this);
        }

        public string GetCommandLineHelp()
        {
            return "<NoticeType> <Color> <VisibleTime> <message>";
        }

        public string GetCommandHelpText()
        {
            return "Broadcasts a server-wide message in a given style.\r\n"
                 + "<NoticeType> is a value from 1-3 (default 3), "
                 + "<Color> is a RGB or ARGB value in Hex, "
                 + "<VisibleTime> is in milliseconds\r\n"
                 + "If the first arguments isn't a valid NoticeType, all arguments are treated as the message using default color and time settings for type 3.\r\n"
                 + "examples:\r\n"
                 + $"{CommandManager.CommandPrefix}announce 3 FF00FF00 5000 Text here use as many spaces as you wish\r\n"
                 + $"{CommandManager.CommandPrefix}announce 3 FFFF00 2500 Text here is in yellow\r\n"
                 + $"{CommandManager.CommandPrefix}announce 3 red 2500 Text here is in red\r\n"
                 + $"{CommandManager.CommandPrefix}announce Automaticly generated lime text";
        }

        public Color NameOrHexColor(String hex)
        {
            try
            {
                Color nameCol = Color.FromName(hex.ToLower());
                if ((nameCol.R != 0) || (nameCol.G != 0) || (nameCol.B != 0))
                    return nameCol; // Only accept try by name when it didn't return pure black

                // remove the # at the front
                hex = hex.Replace("#", "");

                byte a = 255;
                byte r = 255;
                byte g = 255;
                byte b = 255;

                // handle ARGB strings (8 characters long)
                int start = 0;
                if (hex.Length == 8)
                {
                    a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    start = 2;
                }

                // convert RGB characters to bytes
                r = byte.Parse(hex.Substring(start, 2), System.Globalization.NumberStyles.HexNumber);
                g = byte.Parse(hex.Substring(start + 2, 2), System.Globalization.NumberStyles.HexNumber);
                b = byte.Parse(hex.Substring(start + 4, 2), System.Globalization.NumberStyles.HexNumber);

                return Color.FromArgb(a, r, g, b);
            }
            catch
            {
                return Color.White; // Return white on errors
            }
        }

        public void Execute(Character character, string[] args)
        {
            // if no arguments send help information
            if (args.Length == 0)
            {
                character.SendMessage(
                          $"[Announce] syntax: {CommandManager.CommandPrefix}announce [<NoticeType> <Color> <VisibleTime>] <message>\n"
                        + $"[Announce] example1: {CommandManager.CommandPrefix}announce 3 FFFF00 2500 Text here is in yellow\n"
                        + $"[Announce] example2: {CommandManager.CommandPrefix}announce 3 red 2500 Text here is in red\n"
                        + $"[Announce] example3: {CommandManager.CommandPrefix}announce Automaticly generated lime text"
                    );
                return;
            }

            try
            {
                byte type = 0;
                Color color = Color.Lime;
                Int32 visibleTime = 0;
                int firstArg = 3;
                string message = "";

                if (byte.TryParse(args[0], out byte typeval))
                    type = typeval;

                if ((type < 1) || (type > 3))
                {
                    // Invalid type, assume the user only provided text, and handle everything automatically
                    firstArg = 0;
                    type = 3;
                }
                else
                {
                    color = NameOrHexColor(args[1]);
                    if (Int32.TryParse(args[2], out Int32 vistimeval))
                        visibleTime = vistimeval;
                }

                int x = firstArg;
                for (; x < args.Length; x++)
                    message += args[x] + " ";

                // broadcast to all online clients in server
                WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(type, color, visibleTime, message));
                character.SendMessage("[Announce] Sent announcement.");
            }
            catch (Exception x)
            {
                character.SendMessage("|cFFFF0000[Announce] Exception: |r" + x.Message);
            }
        }
    }
}
