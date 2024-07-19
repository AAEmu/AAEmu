using System;
using System.Drawing;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Utils.Scripts;

namespace AAEmu.Game.Scripts.Commands;

public class Scripts : ICommand
{
    public void OnLoad()
    {
        CommandManager.Instance.Register("scripts", this);
    }

    public string GetCommandLineHelp()
    {
        return "<action>";
    }

    public string GetCommandHelpText()
    {
        return "Does script related actions. Allowed <action> are: reload, reboot, save, shutdown";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        if (args.Length == 0)
        {
            character.SendMessage("[Scripts] Using: " + CommandManager.CommandPrefix + "scripts <action>");
            //character.SendMessage("[Scripts] Action: reload");
            return;
        }

        switch (args[0])
        {
            case "reload":
            case "reboot":
                CommandManager.Instance.Clear();
                if (ScriptCompiler.Compile())
                    character.SendMessage("[Scripts] Reload - Success");
                else
                    character.SendMessage("|cFFFF0000[Scripts] Reload - There were errors, please check the server logs for details !|r");
                break;
            case "save":
                if (SaveManager.Instance.DoSave())
                    character.SendMessage("[Scripts] Save - Done saving user database");
                else
                    character.SendMessage("|cFFFF0000[Scripts] Save - Failed saving user database, was possible already in the process of saving, please check server console for details.|r");
                break;
            case "reloadslavepoints":
                SlaveManager.Instance.LoadSlaveAttachmentPointLocations();
                character.SendMessage("[Scripts] Slave Attachment Point Locations .json Reloaded");
                break;
            case "shutdown":
                var shutdownTime = DateTime.UtcNow.AddMinutes(30);
                var doCancel = false;
                if (args.Length > 1)
                {
                    if (args[1].ToLower() == "cancel")
                        doCancel = true;
                    else if (args[1].ToLower() == "now")
                        shutdownTime = DateTime.UtcNow;
                    else if (uint.TryParse(args[1], out var shutdownTimeMinutes))
                        shutdownTime = DateTime.UtcNow.AddMinutes(shutdownTimeMinutes);
                }

                var shutdownTimeRemaining = shutdownTime - DateTime.UtcNow;

                if ((SaveManager.Instance.ShutdownTask != null) && (doCancel))
                {
                    SaveManager.Instance.ShutdownTask.Cancel();
                    character.SendMessage($"[Scripts] Shutdown cancelled.");
                    WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(3, Color.Aqua, 10000, "The server shutdown has been cancelled!"));
                    SaveManager.Instance.ShutdownTask = null;
                    return;
                }
                
                if (doCancel)
                {
                    character.SendMessage($"[Scripts] No shutdown to cancel");
                    return;
                }
                
                if (SaveManager.Instance.ShutdownTask != null)
                {
                    character.SendMessage($"[Scripts] Shutdown was already in progress, changed to {shutdownTimeRemaining.TotalMinutes:F0} minutes from now.");
                    SaveManager.Instance.ShutdownTask.ChangeShutdownTime(shutdownTime.AddSeconds(1));
                    return;
                }

                if (SaveManager.Instance.DoSave())
                {
                    SaveManager.Instance.ShutdownTask = new ShutdownTask(shutdownTime.AddSeconds(5), -1); // Manual Normal Shutdown (-1)
                    TaskManager.Instance.Schedule(SaveManager.Instance.ShutdownTask, null, TimeSpan.FromSeconds(1));
                    character.SendMessage($"[Scripts] Shutdown sequence started, shutting down in |nn;{shutdownTimeRemaining.TotalMinutes:F0}|r minutes");
                }
                else
                {
                    character.SendMessage("[Scripts] Shutdown cancelled because there were save errors, if you want to shutdown regardless without saving, use |nn;/scripts forcedshutdown|r instead.");
                }
                break;
            case "forcedshutdown":
                WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(3, Color.Magenta, 15000, "The server is shutting down right now!"));
                character.SendMessage("[Scripts] Shutting down immediately!");
                Environment.Exit(-2); // Manual Forced Shutdown (-2)
                break;
            default:
                character.SendMessage("|cFFFF0000[Scripts] Undefined action...|r");
                break;
        }
    }
}
