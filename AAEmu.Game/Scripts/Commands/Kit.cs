﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Chat;
using Newtonsoft.Json;
using NLog;
using AAEmu.Game.Utils.Scripts;
using NLog.Targets;

namespace AAEmu.Game.Scripts.Commands;

public class AddKit : ICommand
{
    private readonly Logger Logger = LogManager.GetCurrentClassLogger();

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
        public List<GMItemKitItem> itemkits = new();

        public List<string> itemkitnames = new();

        public void Clear()
        {
            itemkits.Clear();
            itemkitnames.Clear();
        }
    }

    public GMItemKitConfig kitconfig = new();

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

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[Items] " + CommandManager.CommandPrefix + "kit (target) <kitname>");
            character.SendMessage("[Items] " + kitconfig.itemkitnames.Count.ToString() + " kits have been loaded, use " + CommandManager.CommandPrefix + "kit ?  to get a list of kits");
            return;
        }

        Character targetPlayer = WorldManager.GetTargetOrSelf(character, args[0], out var firstarg);

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

        //Logger.Debug("foreach kit in kitconfig.itemkits");
        foreach (var kit in kitconfig.itemkits)
        {
            //Logger.Debug("kit.kitname: " + kit.kitname);
            if (!kit.kitnames.Contains(kitname))
                continue;

            //Logger.Debug("kit.itemID: " + kit.itemID.ToString());

            var itemTemplate = ItemManager.Instance.GetTemplate(kit.itemId);
            if (itemTemplate == null)
            {
                character.SendMessage(ChatType.System, $"Item could not be created, ID: {kit.itemId} !");
                Logger.Error($"itemId not found: {kit.itemId}");
                continue;
            }
            else
            {
                if (!targetPlayer.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Gm, kit.itemId, kit.itemCount, kit.itemGrade))
                {
                    character.SendMessage(ChatType.System, "Item could not be created!", Color.Red);
                    continue;
                }

                if (character.Id != targetPlayer.Id)
                {
                    character.SendMessage($"[Items] added item {kit.itemId} to {targetPlayer.Name}'s inventory");
                    targetPlayer.SendMessage($"[GM] {character.Name} has added a item to your inventory");
                }
                itemsAdded++;
                //Logger.Debug("kit.itemID: " + kit.itemID.ToString()+ " added to " + targetPlayer.Name);
            }

        }

        if (itemsAdded > 0)
        {
            if (character.Id != targetPlayer.Id)
            {
                character.SendMessage($"[Items] added {itemsAdded} items to {targetPlayer.Name}'s inventory");
                targetPlayer.SendMessage($"[GM] {character.Name} has added a {itemsAdded} item to your inventory");
            }
        }
        else
        {
            character.SendMessage($"[Items] No items in kit \"{kitname}\"");
        }

    }

    public void InitKits()
    {
        //Logger.Info("Init");
        kitconfig.Clear();

        var jsonKit = new GMItemKitConfig();
        try
        {
            var filePath = Path.Combine(FileManager.AppPath, "Scripts", "Commands", "kits.json");
            var contents = FileManager.GetFileContents(filePath);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException($"File {filePath} doesn't exists or is empty.");
            jsonKit = JsonConvert.DeserializeObject<GMItemKitConfig>(contents);

            kitconfig.itemkits.AddRange(jsonKit.itemkits);
        }
        catch (Exception x)
        {
            Logger.Error("Exception: " + x.Message);
        }

        // Create a enum for our "/kit ?" help command
        foreach (var kit in kitconfig.itemkits)
        {
            foreach (var kn in kit.kitnames)
                if (!kitconfig.itemkitnames.Contains(kn))
                    kitconfig.itemkitnames.Add(kn);
        }
        kitconfig.itemkitnames.Sort();
    }
}
