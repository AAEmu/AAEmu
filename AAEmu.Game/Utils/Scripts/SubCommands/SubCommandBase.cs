using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using NLog;

namespace AAEmu.Game.Utils.Scripts.SubCommands;

public abstract class SubCommandBase : ICommandV2
{
    protected Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly Dictionary<string, ICommandV2> _subCommands = new();
    private List<SubCommandParameterBase> _parameters = new();

    protected void AddParameter(SubCommandParameterBase parameter)
    {
        if (parameter.Prefix is null &&
            parameter.IsRequired &&
            _parameters.Count != 0 &&
            _parameters.LastOrDefault(x => !x.IsRequired && x.Prefix is null) is not null)
        {
            throw new InvalidOperationException("Cannot add a required parameter after an optional parameter");
        }

        if (parameter.Prefix is not null &&
            parameter.IsRequired &&
            _parameters.Count != 0 &&
            _parameters.LastOrDefault(x => !x.IsRequired && x.Prefix is not null) is not null)
        {
            throw new InvalidOperationException("Cannot add a required prefix parameter after an optional prefix parameter");
        }

        _parameters.Add(parameter);
    }

    protected List<string> SupportedCommands => _subCommands.Keys.ToList();
    protected string Title { get; set; }
    public string Description { get; protected set; }
    public string CallPrefix { get; protected set; }

    public SubCommandBase()
    {
    }

    public SubCommandBase(Dictionary<ICommandV2, string[]> registerSubCommands)
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
    protected void Register(ICommandV2 command, params string[] aliases)
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

    public void PreExecute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput)
    {
        try
        {
            //Verifies if the next firstargument has a subcommand to implement it
            var firstArgument = args.FirstOrDefault();
            if (firstArgument is not null)
            {
                if (firstArgument.ToLower() == "help")
                {
                    SendHelpMessage(messageOutput);
                }
                else if (_subCommands.TryGetValue(firstArgument, out var command))
                {
                    command.PreExecute(character, firstArgument, args.Skip(1).ToArray(), messageOutput);
                }
                else
                {
                    if (_parameters.Count > 0)
                    {
                        var parameterResults = LoadParametersValues(args);
                        if (PreValidate(messageOutput, parameterResults.Values))
                        {
                            Execute(character, triggerArgument, parameterResults.ToDictionary(x => x.Key, x => x.Value.Value), messageOutput);
                        }
                    }
                    else
                    {
                        // Backwards compatibility with non parameter subcommands
                        Execute(character, triggerArgument, args, messageOutput);
                    }
                }
            }
            else
            {
                Execute(character, triggerArgument, args, messageOutput);
            }
        }
        catch (Exception ex)
        {
            SendColorMessage(messageOutput, Color.Red, $"Unexpected error: {ex.Message}");
            Logger.Error(ex);
        }
    }

    protected IDictionary<string, ParameterResult> LoadParametersValues(string[] args)
    {
        Dictionary<string, ParameterResult> parametersValue = new();

        var nonPrefixArguments = args.Where(a => !a.Contains('=')).ToArray();
        var parameterCount = 0;
        foreach (var parameter in _parameters.Where(p => p.Prefix is null))
        {
            ParameterResult parameterValue = null;
            if (parameterCount < nonPrefixArguments.Length)
            {
                // parameters provided
                parameterValue = parameter.Load(nonPrefixArguments[parameterCount]);
            }
            else if (parameter.IsRequired)
            {
                //required parameters that were not provided
                parameterValue = new ParameterResult<object>(parameter.Name, null, $"Parameter {parameter.Name} is required");
            }

            if (parameterValue is not null)
            {
                parametersValue.Add(parameterValue.Name, parameterValue);
            }
            parameterCount++;
        }

        // Find prefixed parameters (could be anywhere in the list of parameters)
        var prefixArguments = args.Where(a => a.IndexOf('=') > -1).ToArray();
        foreach (var parameterPrefix in _parameters.Where(p => p.Prefix is not null))
        {
            string foundPrefixArgument = null;
            bool duplicatedPrefixValue = false;
            foreach (var argument in prefixArguments)
            {
                if (parameterPrefix.MatchPrefix(argument))
                {
                    duplicatedPrefixValue = foundPrefixArgument is not null;
                    foundPrefixArgument = argument;
                }
            }

            if (foundPrefixArgument is not null)
            {
                ParameterResult parameterValue = null;
                if (duplicatedPrefixValue)
                {
                    parameterValue = new ParameterResult<object>(parameterPrefix.Name, null, $"Parameter prefix {parameterPrefix.Prefix} is duplicated");
                }
                else
                {
                    parameterValue = parameterPrefix.Load(foundPrefixArgument);
                }
                parametersValue.Add(parameterValue.Name, parameterValue);
            }
        }

        //Find optional parameters to default values
        foreach (var parameterDefaulted in _parameters.Where(p => !p.IsRequired && p.DefaultValue is not null && !parametersValue.ContainsKey(p.Name)))
        {
            var parameterValue = new ParameterResult<object>(parameterDefaulted.Name, parameterDefaulted.DefaultValue, null);
            parametersValue.Add(parameterValue.Name, parameterValue);
        }

        return parametersValue;
    }

    protected bool PreValidate(IMessageOutput messageOutput, ICollection<ParameterResult> parameters)
    {
        var isValid = true;
        foreach (var parameter in parameters.Where(p => !p.IsValid))
        {
            isValid = false;
            SendColorMessage(messageOutput, Color.Red, parameter.InvalidMessage);
        }
        return isValid;
    }
    protected virtual void SendHelpMessage(IMessageOutput messageOutput)
    {
        SendColorMessage(messageOutput, Color.LawnGreen, Description);
        if (_parameters.Count > 0)
        {
            SendColorMessage(messageOutput, Color.LawnGreen, GetCallExample());
        }
        if (SupportedCommands.Count > 0)
        {
            SendColorMessage(messageOutput, Color.LawnGreen, $"Supported subcommands: <{string.Join("||", SupportedCommands)}>");
            SendColorMessage(messageOutput, Color.LawnGreen, $"For more details use /<command> <subcommand> help.");
        }
    }

    private string GetCallExample()
    {
        StringBuilder callExampleMessage = new(CallPrefix);
        if (_parameters.Count > 0)
        {
            foreach (var parameter in _parameters.OrderBy(p => p.Prefix is not null).ThenBy(p => !p.IsRequired))
            {
                callExampleMessage.Append(parameter.IsRequired
                    ? $" <{parameter.CallExample}>"
                    : $" [{parameter.CallExample}]");
            }
        }
        return callExampleMessage.ToString();
    }

    /// <summary>
    /// Implementation related to this command level
    /// </summary>
    /// <param name="character">character reference</param>
    /// <param name="triggerArgument">argument that triggered this subcommand</param>
    /// <param name="args">additional arguments</param>
    /// <param name="messageOutput">Output Handler</param>
    public virtual void Execute(ICharacter character, string triggerArgument, string[] args, IMessageOutput messageOutput)
    {
        SendHelpMessage(messageOutput);
    }

    /// <summary>
    /// Implementation related to this command level
    /// </summary>
    /// <param name="character">character reference</param>
    /// <param name="triggerArgument">argument that triggered this subcommand</param>
    /// <param name="parameters">additional arguments</param>
    /// <param name="messageOutput"></param>
    public virtual void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters, IMessageOutput messageOutput)
    {
        SendHelpMessage(messageOutput);
    }

    /// <summary>
    /// Adds the subcommand prefix to the message
    /// </summary>
    /// <param name="messageOutput">Output handler</param>
    /// <param name="message">Message to send to the character</param>
    protected void SendMessage(IMessageOutput messageOutput, string message)
    {
        messageOutput.SendMessage($"{Title} {message}");
    }

    /// <summary>
    /// Adds the subcommand prefix to the message
    /// </summary>
    /// <param name="target">character reference</param>
    /// <param name="messageOutput">Output handler</param>
    /// <param name="message">Message to send to the character</param>
    protected void SendMessage(ICharacter target, IMessageOutput messageOutput, string message)
    {
        messageOutput.SendMessage(target, $"{Title} {message}");
    }

    /// <summary>
    /// Wrapper to send colored messages to the character (useful for error messages)
    /// </summary>
    /// <param name="messageOutput"></param>
    /// <param name="color">Color to display the message</param>
    /// <param name="message">Message to send to the character</param>
    protected void SendColorMessage(IMessageOutput messageOutput, Color color, string message)
    {
        messageOutput.SendMessage(ChatType.System, $"{Title} {message}", color);
    }

    protected static string GetOptionalArgumentValue(string[] args, string argumentName, string defaultArgumentValue)
    {
        var argumentValue = args.Where(a => a.StartsWith(argumentName + "=")).FirstOrDefault();
        if (argumentValue is null)
        {
            return defaultArgumentValue;
        }

        return argumentValue.Split('=')[1].Trim();
    }

    protected static float GetOptionalArgumentValue(string[] args, string argumentName, float defaultArgumentValue)
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

    protected static T GetOptionalParameterValue<T>(IDictionary<string, ParameterValue> parameters, string parameterName, T defaultArgumentValue)
    {
        if (!parameters.ContainsKey(parameterName))
            return defaultArgumentValue;

        return parameters[parameterName].As<T>();
    }
}
