﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands;
using NLog;

namespace AAEmu.Game.Utils.Scripts
{
    public abstract class SubCommandBase : ISubCommand
    {
        protected Logger _log = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<string, ISubCommand> _subCommands = new();
        protected SubCommandParameterConfig _parametersConfig = new();
        protected List<string> SupportedCommands => _subCommands.Keys.ToList();
        protected string Prefix { get; set; }
        public string Description { get; protected set; }
        public string CallExample { get; protected set; }

        public SubCommandBase()
        {
        }

        public SubCommandBase(Dictionary<ISubCommand, string[]> registerSubCommands)
        {
            foreach (var subCommand in registerSubCommands)
            {
                Register(subCommand.Key, subCommand.Value);
            }
        }
        /// <summary>
        /// Register any subcommands chained to this
        /// </summary>
        /// <param name="command">Command impl</param>
        /// <param name="aliases">All supported aliases for triggering the command</param>
        protected void Register(ISubCommand command, params string[] aliases)
        {
            foreach (var alias in aliases.Select(a => a.ToLower()))
            {
                if (_subCommands.ContainsKey(alias))
                {
                    _subCommands.Remove(alias);
                }
                _subCommands.Add(alias, command);
            }
        }

        public virtual void PreExecute(ICharacter character, string triggerArgument, string[] args)
        {
            //Verifies if the next firstargument has a subcommand to implement it
            var firstArgument = args.FirstOrDefault();
            if (firstArgument is not null) 
            {
                if (firstArgument.ToLower() == "help") 
                {
                    SendHelpMessage(character);
                }
                else if (_subCommands.ContainsKey(firstArgument))
                {
                    _subCommands[firstArgument].PreExecute(character, firstArgument, args.Skip(1).ToArray());
                }
                else
                {
                    Execute(character, triggerArgument, args);
                }
            }
            else
            {
                Execute(character, triggerArgument, args);
            }
        }

        protected virtual void SendHelpMessage(ICharacter character)
        {
            SendMessage(character, Description);
            SendMessage(character, CallExample);
            if (SupportedCommands.Count > 0) 
            {
                SendMessage(character, $"Supported subcommands: <{string.Join("||", SupportedCommands)}>");
                SendMessage(character, $"For more details use /<command> [<subcommand>] help.");
            }
        }

        /// <summary>
        /// Implementation related to this command level
        /// </summary>
        /// <param name="character">character reference</param>
        /// <param name="triggerArgument">argument that triggered this subcommand</param>
        /// <param name="args">additional arguments</param>
        public virtual void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            if (_subCommands.Count > 0)
            {
                SendHelpMessage(character);
            }
        }

        /// <summary>
        /// Adds the subcommand prefix to the message
        /// </summary>
        /// <param name="character">character reference</param>
        /// <param name="message">Message to send to the character</param>
        /// <param name="parameters">Message parameters</param>
        protected void SendMessage(ICharacter character, string message, params object[] parameters)
        {
            character.SendMessage($"{Prefix} {message}", parameters);
        }

        /// <summary>
        /// Wrapper to send colored messages to the character (useful for error messages)
        /// </summary>
        /// <param name="character">character reference</param>
        /// <param name="color">Color to display the message</param>
        /// <param name="message">Message to send to the character</param>
        /// <param name="parameters">Message parameters</param>
        protected void SendColorMessage(ICharacter character, Color color, string message, params object[] parameters)
        {
            character.SendMessage(color, $"{Prefix} {message}", parameters);
        }

        protected string GetOptionalArgumentValue(string[] args, string argumentName, string defaultArgumentValue)
        {
            var argumentValue = args.Where(a => a.StartsWith(argumentName + "=")).FirstOrDefault();
            if (argumentValue is null)
            {
                return defaultArgumentValue;
            }

            return argumentValue.Split('=')[1].Trim();
        }

        protected float GetOptionalArgumentValue(string[] args, string argumentName, float defaultArgumentValue)
        {
            var argumentValueText = args.Where(a => a.StartsWith(argumentName + "=")).FirstOrDefault();
            if (argumentValueText is null)
            {
                return defaultArgumentValue;
            }

            if (float.TryParse(argumentValueText.Split('=')[1], out var argumentValue))
            {
                return argumentValue;
            }
            else
            {
                throw new ArgumentException($"Invalid value [{argumentValueText}] for parameter {argumentName} ");
            }
        }
    }
}
