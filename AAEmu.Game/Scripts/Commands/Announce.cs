/*
* Announce.cs
* Author: SargeDG
* Updated by: ZeromusXYZ
* usage: /Announce <NoticeType> <Color> <VisibleTime> <message>
* usage example: /announce 3 FFFF00 2500 Text here is in yellow
*  
* 
* [Information]
* Check SCNoticeMessagePacket for information on how the packet
* is constructed. It send as follows
* 1 byte for the message type (default seems to be 3, but we don't know what the values mean)
* 2 chars for a hex-string for alpha of the text color
* 1 int for the time before it fades (in ms)
* 6 chars for a hex-string RGB value of the text color
* remainder is the message itself
* 
*/

using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using System;
using AAEmu.Game.Models.Game.Chat;
using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers.World;
using System.Drawing;

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
            return "Broadcasts a server-wide message in a given style.\r\n" +
                "<NoticeType> is a value from 1-3 (default 3), " +
                "<Color> is a RGB or ARGB value in Hex, " +
                "<VisibleTime> is in milliseconds\r\n" +
                "If the first arguments isn't a valid NoticeType, all arguments are treated as the message using default color and time settings for type 3.\r\n" +
                "examples:\r\n" +
                CommandManager.CommandPrefix + "announce 3 FF00FF00 5000 Text here use as many spaces as you wish\r\n" +
                CommandManager.CommandPrefix + "announce 3 FFFF00 2500 Text here is in yellow\r\n" +
                CommandManager.CommandPrefix + "announce 3 red 2500 Text here is in red\r\n" +
                CommandManager.CommandPrefix + "announce Automaticly generated lime text";
        }

        public Color NameOrHexColor(String hex)
        {
            try
            {
                Color nameCol = Color.FromName(hex.ToLower());
                if ((nameCol.R != 0) || (nameCol.G != 0) || (nameCol.B != 0))
                    return nameCol; // Only accept try by name when it didn't return pure black

                //remove the # at the front
                hex = hex.Replace("#", "");

                byte a = 255;
                byte r = 255;
                byte g = 255;
                byte b = 255;

                int start = 0;

                //handle ARGB strings (8 characters long)
                if (hex.Length == 8)
                {
                    a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    start = 2;
                }

                //convert RGB characters to bytes
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
            //if no arguments send help information
            if (args.Length == 0)
            {                                
                character.SendMessage("[Announce] syntax: " + CommandManager.CommandPrefix + "announce [<NoticeType> <Color> <VisibleTime>] <message>");
                character.SendMessage("[Announce] example1: " + CommandManager.CommandPrefix + "announce 3 FFFF00 2500 Text here is in yellow");
                character.SendMessage("[Announce] example2: " + CommandManager.CommandPrefix + "announce 3 red 2500 Text here is in red");
                character.SendMessage("[Announce] example3: " + CommandManager.CommandPrefix + "announce Automaticly generated lime text");
                return;
            }

            try
            {
                // initialze variables
                byte _type = 0;
                Color _color = Color.Lime;
                Int32 _visibletime = 0;
                int firstArg = 3;
                string _message = "";

                if (byte.TryParse(args[0], out byte typeval))
                    _type = typeval;

                if ((_type < 1) || (_type > 3))
                {
                    // Invalid type, assume the user only provided text, and handle everything autmatically
                    firstArg = 0;
                    _type = 3;
                }
                else
                {
                    _color = NameOrHexColor(args[1]);
                    if (Int32.TryParse(args[2], out Int32 vistimeval))
                        _visibletime = vistimeval;
                }

                int x = firstArg;
                for (; x < args.Length; x++)
                    _message += args[x] + " ";

                //broadcast to all online clients in server
                WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(_type, _color, _visibletime, _message));

                //send back confirmation script executed to script runner
                character.SendMessage("[Announce] Script Executed.");
            }
            catch (Exception x)
            {
                character.SendMessage("|cFFFF0000[Announce] Exception: |r" + x.Message);
            }
        }
    }
}
