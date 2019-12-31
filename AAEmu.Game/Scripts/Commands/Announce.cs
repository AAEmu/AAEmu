/*
* Announce.cs
* Author: SargeDG
* usage: /Announce <Notice Type (3)> <Text opacity  (9)> <Visible time miliseconds (1000)>   <color Hex code + text message string (#00ff0dText here use as many spaces as you wish)>
* usage example: /announce 3 9 5000 #00ff0dText here use as many spaces as you wish
*  
* 
* [Information]
* _message is handled by the packet in such a way
* were the first seven chars of _message are taken
* as a Hex Color string(#00ff0d) 
* 
* those chars will not be displayed, for example
* if we use the command (/announce 3 9 5000 server restart 20 mins)
* then the text string displayed by SCNoticeMessagePacket
* will display the string "restart 20 mins"
* 
* with hex color code appended to _message argument 
* [example]
* /announce 3 9 5000 #00ff0d server restart 20 mins
* [/example]
* the text string displayed by SCNoticeMessagePacket
* will display the string "server restart 20 mins" 
* 
* 
* Note: Hex Holor code seems to work fine with 
* _color argument set to 9, this arg is wierd it seems to not only be able to control opacity 
* but also affect color of text outside of the hex color code but in conjunction with the hex color code
* 
* This example will display the text in bright green acording to hex color code
* [example]
* /announce 3 9 5000 #00ff0d server restart 20 mins
* [/example]
* 
* while this example will display the text in a dark blue 
* [example]
* /announce 3 99 5000 #00ff0d server restart 20 mins
* [/example]
* 
* and this example will change the opacity of the text
* [example]
* /announce 3 4 5000 #00ff0d server restart 20 mins
* [/example]
* 
* 
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
            CommandManager.Instance.Register("Announce", this);
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
                "/announce 3 FF00FF00 5000 Text here use as many spaces as you wish\r\n" +
                "/announce 3 FFFF00 2500 Text here is in yellow\r\n" +
                "/announce 3 red 2500 Text here is in red\r\n" +
                "/announce Automaticly generated lime text";
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
                character.SendMessage("[Announce] syntax: /announce [<NoticeType> <Color> <VisibleTime>] <message>");
                character.SendMessage("[Announce] example1: /announce 3 FFFF00 2500 Text here is in yellow");
                character.SendMessage("[Announce] example2: /announce 3 red 2500 Text here is in red");
                character.SendMessage("[Announce] example3: /announce Automaticly generated lime text");
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
