using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using NLog;
using System;

namespace AAEmu.Game.Scripts.Commands;

internal class ReloadConfigs : ICommand
{
    public string[] CommandNames { get; set; } = ["reloadconfig", "reload_configs", "reload_configurations"];
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "";
    }

    public string GetCommandHelpText()
    {
        return "Reloads the ConfigurationManager";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        try
        {
            if (Program.LoadConfiguration())
            {
                //ConfigurationManager.Instance.Load();
                CommandManager.SendNormalText(this, messageOutput, "Configuration reloaded");
            }
            else
            {
                CommandManager.SendErrorText(this, messageOutput,
                    "Configurations failed reloading - check error output");
            }
        }
        catch (Exception e)
        {
            Logger.Error(e.Message);
            CommandManager.SendErrorText(this, messageOutput, "Configurations failed reloading - check error output");
        }
    }
}
