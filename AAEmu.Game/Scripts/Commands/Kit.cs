using System;
using System.IO;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;
using Newtonsoft.Json;
using NLog;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddKit : ICommand
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();

        public class GMItemKitItem
        {
            [JsonProperty("names")]
            public List<string> kitnames { get; set; }
            [JsonProperty("id")]
            public uint itemId { get; set; }
            [JsonProperty("grade")]
            public byte itemGrade { get; set; }
            [JsonProperty("count")] 
            public int itemCount { get; set; }

            public GMItemKitItem()
            {
                kitnames = new List<string>();
                itemId = 0;
                itemGrade = 0;
                itemCount = 1;
            }

            public GMItemKitItem(string kit, uint id, byte grade = 0, int count = 1)
            {
                this.kitnames.Clear();
                this.kitnames.Add(kit.ToLower());
                this.itemId = id;
                this.itemGrade = grade;
                this.itemCount = count;
            }
        }
        
        public class GMItemKitConfig
        {
            [JsonProperty("itemkits")]
            public List<GMItemKitItem> itemkits = new List<GMItemKitItem>();

            public List<string> itemkitnames = new List<string>();

            public void Clear()
            {
                itemkits.Clear();
                itemkitnames.Clear();
            }
        }

        public GMItemKitConfig kitconfig = new GMItemKitConfig();
        
        public void OnLoad()
        {
            string[] name = { "kit", "addkit", "add_kit" };
            CommandManager.Instance.Register(name, this);

            InitKits();
        }

        public string GetCommandLineHelp()
        {
            return "(target) <kitname>";
        }

        public string GetCommandHelpText()
        {
            return "Adds a set of items based on a kit name to target player.";
        }

        public void Execute(Character character, string[] args)
        {
            if (args.Length == 0)
            {
                character.SendMessage("[Items] " + CommandManager.CommandPrefix + "kit (target) <kitname>");
                character.SendMessage("[Items] " + kitconfig.itemkitnames.Count.ToString() + " kits have been loaded, use " + CommandManager.CommandPrefix + "kit ?  to get a list of kits");
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            string kitname = string.Empty;
            int itemsAdded = 0;

            if (args.Length > firstarg + 0)
                kitname = args[firstarg + 0].ToLower();

            if (kitname == "?")
            {
                character.SendMessage("[Items] " + CommandManager.CommandPrefix + "kit has the following kits registered:");
                string s = string.Empty;
                foreach (var n in kitconfig.itemkitnames)
                    s += n + "  ";
                character.SendMessage("|cFFFFFFFF" + s + "|r");
                return;
            }

            //_log.Debug("foreach kit in kitconfig.itemkits");
            foreach (var kit in kitconfig.itemkits)
            {
                //_log.Debug("kit.kitname: " + kit.kitname);
                if (!kit.kitnames.Contains(kitname))
                    continue;

                //_log.Debug("kit.itemID: " + kit.itemID.ToString());

                var itemTemplate = ItemManager.Instance.GetTemplate(kit.itemId);
                if (itemTemplate == null)
                {
                    character.SendMessage("|cFFFF0000Item could not be created, ID: {0} ! |r", kit.itemId);
                    _log.Error("itemId not found: " + kit.itemId.ToString());
                    continue;
                }
                else
                {
                    if (!targetPlayer.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, kit.itemId, kit.itemCount, kit.itemGrade))
                    {
                        character.SendMessage("|cFFFF0000Item could not be created!|r");
                        continue;
                    }

                    if (character.Id != targetPlayer.Id)
                    {
                        character.SendMessage("[Items] added item {0} to {1}'s inventory", kit.itemId, targetPlayer.Name);
                        targetPlayer.SendMessage("[GM] {0} has added a item to your inventory", character.Name);
                    }
                    itemsAdded++;
                    //_log.Debug("kit.itemID: " + kit.itemID.ToString()+ " added to " + targetPlayer.Name);
                }

            }

            if (itemsAdded > 0)
            {
                if (character.Id != targetPlayer.Id)
                {
                    character.SendMessage("[Items] added {0} items to {1}'s inventory", itemsAdded, targetPlayer.Name);
                    targetPlayer.SendMessage("[GM] {0} has added a {1} item to your inventory", character.Name, itemsAdded);
                }
            }
            else
            {
                character.SendMessage("[Items] No items in kit \"{0}\"", kitname);
            }

        }

        public void InitKits()
        {
            //_log.Info("Init");
            kitconfig.Clear();
            
            GMItemKitConfig jsonkit = new GMItemKitConfig();
            try
            {
                string data = File.ReadAllText("Scripts\\Commands\\kits.json");
                // _log.Info("data: " + data);
                jsonkit = JsonConvert.DeserializeObject<GMItemKitConfig>(data);
                kitconfig.itemkits.AddRange(jsonkit.itemkits);
            }
            catch (Exception x)
            {
                _log.Error("Exception: " + x.Message);
            }
            
            // Create a enum for our "/kit ?" help command
            foreach(var kit in kitconfig.itemkits)
            {
                foreach (var kn in kit.kitnames)
                    if (!kitconfig.itemkitnames.Contains(kn))
                        kitconfig.itemkitnames.Add(kn);
            }
            kitconfig.itemkitnames.Sort();

        }
    }
}
