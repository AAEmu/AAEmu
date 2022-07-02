﻿using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.Game.Scripts.Commands
{
    public class DoodadCmd : SubCommandBase, ICommand, ISubCommand
    {
        public DoodadCmd() 
        {
            Prefix = "[Doodad]";
            Description = "Root command to manage Doodads";
            CallExample = "/doodad [chain||setphase||save||pos]";

            Register(new DoodadChainSubCommand(), "chain");
            Register(new DoodadPhaseSubCommand(), "phase", "setphase");
            Register(new DoodadSaveSubCommand(), "save");
            Register(new DoodadPositionSubCommand(), "pos");
        }

        public void OnLoad()
        {
            CommandManager.Instance.Register("doodad", this);
        }

        public DoodadCmd(Dictionary<ISubCommand, string[]> subcommands) : base(subcommands)
        {

        }

        public string GetCommandLineHelp()
        {
            return $"<{string.Join("||", SupportedCommands)}>";
        }

        public string GetCommandHelpText()
{
            return "[Doodad] /doodad [chain <TemplateId>]||[setphase||save <ObjId>]||[pos <ObjId> <x> <y> <z> <rx> <ry> <rz>] - Use x y z roll pitch yaw instead of a value to keep current position";
        }

        public void Execute( Character character, string[] args )
        {
            try
            {
                base.PreExecute(character, "doodad", args);
            }
            catch (Exception e)
            {
                character.SendMessage(e.Message);
            }
            //DoodadCommandUtil.GetCommandChoice( character, args[0], args );
        }
    }
}
