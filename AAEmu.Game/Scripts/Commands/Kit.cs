using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Scripts.Commands
{
    public class AddKit : ICommand
    {

        public class GMItemKitItem
        {
            public string kitName;
            public uint itemID;
            public byte itemGrade;
            public int itemCount;

            public GMItemKitItem(string name, uint id, byte grade = 0, int count = 1)
            {
                kitName = name.ToLower();
                itemID = id;
                itemGrade = grade;
                itemCount = count;
            }
        }

        public List<GMItemKitItem> kits = new List<GMItemKitItem>();

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
                return;
            }

            Character targetPlayer = WorldManager.Instance.GetTargetOrSelf(character, args[0], out var firstarg);

            string kitname = string.Empty;
            int itemsAdded = 0;

            if (args.Length > firstarg + 0)
                kitname = args[firstarg + 0].ToLower();

            foreach(var kit in kits)
            {
                if (kit.kitName != kitname)
                    continue;

                var item = ItemManager.Instance.Create(kit.itemID, kit.itemCount, kit.itemGrade, true);
                if (item == null)
                {
                    character.SendMessage("|cFFFF0000Item could not be created, ID: {0} ! |r", kit.itemID);
                    continue;
                }
                else
                {
                    var res = targetPlayer.Inventory.AddItem(item);
                    if (res == null)
                    {
                        ItemIdManager.Instance.ReleaseId((uint)item.Id);
                        continue;
                    }

                    var tasks = new List<ItemTask>();
                    if (res.Id != item.Id)
                        tasks.Add(new ItemCountUpdate(res, item.Count));
                    else
                        tasks.Add(new ItemAdd(item));

                    targetPlayer.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.AutoLootDoodadItem, tasks, new List<ulong>()));
                    if (character.Id != targetPlayer.Id)
                    {
                        character.SendMessage("[Items] added item {0} to {1}'s inventory", kit.itemID, targetPlayer.Name);
                        targetPlayer.SendMessage("[GM] {0} has added a item to your inventory", character.Name);
                    }
                    itemsAdded++;
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
                character.SendMessage("[Items] No items where for kit \"{0}\"", kitname);
            }

        }

        public void InitKits()
        {
            kits.Clear();
            kits.Add(new GMItemKitItem("test", 23865));
            kits.Add(new GMItemKitItem("test", 23869));
        }
    }
}
