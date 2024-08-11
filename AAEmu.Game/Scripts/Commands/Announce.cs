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
 * is constructed. It sends as follows
 * 1 byte for the message type (default seems to be 3, but we don't know what the values mean)
 * 2 chars for a hex-string for alpha of the text color
 * 1 int for the time before it fades (in ms)
 * 6 chars for a hex-string RGB value of the text color
 * remainder is the message itself
 *
 */

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using System;
using AAEmu.Game.Core.Managers.World;
using System.Drawing;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Announce : ICommand
{
    public string[] CommandNames { get; set; } = new string[] { "announce" };

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
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
               CommandManager.CommandPrefix +
               $"{CommandNames[0]} 3 FF00FF00 5000 Text here use as many spaces as you wish\r\n" +
               CommandManager.CommandPrefix + $"{CommandNames[0]} 3 FFFF00 2500 Text here is in yellow\r\n" +
               CommandManager.CommandPrefix + $"{CommandNames[0]} 3 red 2500 Text here is in red\r\n" +
               CommandManager.CommandPrefix + $"{CommandNames[0]} Automatically generated lime text";
    }

    private static Color NameOrHexColor(string hex)
    {
        try
        {
            var nameCol = Color.FromName(hex.ToLower());
            if (nameCol.R != 0 || nameCol.G != 0 || nameCol.B != 0)
            {
                return nameCol; // Only accept try by name when it didn't return pure black
            }

            //remove the # at the front
            hex = hex.Replace("#", "");

            byte a = 255;
            byte r = 255;
            byte g = 255;
            byte b = 255;

            var start = 0;

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

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        //if no arguments send help information
        if (args.Length == 0)
        {
            CommandManager.SendDefaultHelpText(this, messageOutput);
            return;
        }

        try
        {
            // initialize variables
            byte messageType = 0;
            var messageColor = Color.Lime;
            var messageVisibleTime = 0;
            var firstArg = 3;
            var message = "";

            if (byte.TryParse(args[0], out var typeVal))
            {
                messageType = typeVal;
            }

            if (messageType < 1 || messageType > 3)
            {
                // Invalid type, assume the user only provided text, and handle everything automatically
                firstArg = 0;
                messageType = 3;
            }
            else
            {
                messageColor = NameOrHexColor(args[1]);
                if (int.TryParse(args[2], out var visTimeVal))
                {
                    messageVisibleTime = visTimeVal;
                }
            }

            var x = firstArg;
            for (; x < args.Length; x++)
            {
                message += args[x] + " ";
            }

            //broadcast to all online clients in server
            WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(messageType, messageColor,
                messageVisibleTime, message));

            //send back confirmation script executed to script runner
            CommandManager.SendNormalText(this, messageOutput, $"Announcement sent.");
        }
        catch (Exception x)
        {
            CommandManager.SendErrorText(this, messageOutput, $"Exception: {x.Message}");
        }
    }
}
