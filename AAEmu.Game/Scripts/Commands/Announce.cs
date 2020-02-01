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

namespace AAEmu.Game.Scripts.Commands
         
{
    public class Announce : ICommand
    {
        public void OnLoad()
        {
            CommandManager.Instance.Register("Announce", this);
        }
        
        public void Execute(Character character, string[] args)
        {
            //if no arguments send help information
            if (args.Length == 0)
            {                                
                character.SendMessage("[Announce] syntax: /Announce <Notice Type(3)> <Text opacity(9)> <Visible time miliseconds(1000)> <color Hex code(#00ff0d)> <text message>");
                character.SendMessage("[Announce] usage example: /Announce 3 9 5000 #00ff0d Text here use as many spaces as you wish ");
                return;
            }


            // initialze variables           
            var _type = Convert.ToByte(args[0]);
            var _color = args[1].ToString();
            var _Vistime = Convert.ToInt32(args[2]);
            var _message = args[3].ToString();
                      
            // Handle Spaces in args _message                             
             var s = 0;
             for (var x = 0; x < args.Length; x++)
             {
                s = x;
                if (s > 3)
                {
                    _message = _message + " " + args[s];
                }
             }


             
            //boradcast to all online clients in server
            WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(_type, _color, _Vistime, _message));
            
            //send back confirmation script executed to script runner
            character.SendMessage("[Announce] Script Executed.");
        }
    }
}
