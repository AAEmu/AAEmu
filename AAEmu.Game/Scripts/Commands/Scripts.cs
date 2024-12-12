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
    public string[] CommandNames { get; set; } = ["scripts"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
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
                {
                    CommandManager.SendNormalText(this, messageOutput, "[Scripts] Reload - Success");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        "Reload - There were errors, please check the server logs for details !");
                }

                break;
            case "save":
                if (SaveManager.Instance.DoSave())
                {
                    CommandManager.SendNormalText(this, messageOutput, "Save - Done saving user database");
                }
                else
                {
                    CommandManager.SendErrorText(this, messageOutput,
                        "Save - Failed saving user database, was possible already in the process of saving, please check server console for details.");
                }

                break;
            case "reloadslavepoints":
                SlaveManager.Instance.LoadSlaveAttachmentPointLocations();
                CommandManager.SendNormalText(this, messageOutput,
                    "Slave Attachment Point Locations reloaded from .json file");
                break;
            case "shutdown":
                var shutdownTime = DateTime.UtcNow.AddMinutes(30);
                var doCancel = false;
                if (args.Length > 1)
                {
                    if (args[1].Equals("cancel", StringComparison.CurrentCultureIgnoreCase))
                    {
                        doCancel = true;
                    }
                    else if (args[1].Equals("now", StringComparison.CurrentCultureIgnoreCase))
                    {
                        shutdownTime = DateTime.UtcNow;
                    }
                    else if (uint.TryParse(args[1], out var shutdownTimeMinutes))
                    {
                        shutdownTime = DateTime.UtcNow.AddMinutes(shutdownTimeMinutes);
                    }
                }

                var shutdownTimeRemaining = shutdownTime - DateTime.UtcNow;

                if (SaveManager.Instance.ShutdownTask != null && doCancel)
                {
                    SaveManager.Instance.ShutdownTask.Cancel();
                    CommandManager.SendNormalText(this, messageOutput, $"Shutdown cancelled.");
                    WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(3, Color.Aqua, 10000,
                        "The server shutdown has been cancelled!"));
                    SaveManager.Instance.ShutdownTask = null;
                    return;
                }

                if (doCancel)
                {
                    CommandManager.SendErrorText(this, messageOutput, $"No shutdown to cancel");
                    return;
                }

                if (SaveManager.Instance.ShutdownTask != null)
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Shutdown was already in progress, changed to {shutdownTimeRemaining.TotalMinutes:F0} minutes from now.");
                    SaveManager.Instance.ShutdownTask.ChangeShutdownTime(shutdownTime.AddSeconds(1));
                    return;
                }

                if (SaveManager.Instance.DoSave())
                {
                    SaveManager.Instance.ShutdownTask =
                        new ShutdownTask(shutdownTime.AddSeconds(5), -1); // Manual Normal Shutdown (-1)
                    TaskManager.Instance.Schedule(SaveManager.Instance.ShutdownTask, null, TimeSpan.FromSeconds(1));
                    CommandManager.SendNormalText(this, messageOutput,
                        $"Shutdown sequence started, shutting down in |nn;{shutdownTimeRemaining.TotalMinutes:F0}|r minutes");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput,
                        "Shutdown cancelled because there were save errors, if you want to shutdown regardless without saving, use |nn;/scripts forcedshutdown|r instead.");
                }

                break;
            case "forcedshutdown":
                WorldManager.Instance.BroadcastPacketToServer(new SCNoticeMessagePacket(3, Color.Magenta, 15000,
                    "The server is shutting down right now!"));
                CommandManager.SendNormalText(this, messageOutput, "Shutting down immediately!");
                Environment.Exit(-2); // Manual Forced Shutdown (-2)
                break;
            default:
                CommandManager.SendErrorText(this, messageOutput, "Undefined action...");
                break;
        }
    }
}
