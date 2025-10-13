using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;
using NLog;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// AI聊天测试命令 - 用于测试游戏内容集成功能
/// </summary>
public class AIChatTest : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public string[] CommandNames { get; set; } = ["aitest", "aichattest"];

    public string GetCommandLineHelp()
    {
        return "[功能]";
    }

    public string GetCommandHelpText()
    {
        return "AI聊天测试命令 - 用于测试AI聊天系统的各项功能\n" +
               "使用方法: /aitest [功能]\n" +
               "可用功能:\n" +
               "  status - 显示AI聊天系统状态\n" +
               "  gamecontent - 测试游戏内容集成功能\n" +
               "  config - 显示当前配置\n" +
               "  sessions - 显示当前会话统计";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        try
        {
            if (args.Length == 0)
            {
                ShowHelp(character, messageOutput);
                return;
            }

            var command = args[0].ToLower();
            
            switch (command)
            {
                case "status":
                    ShowAIChatStatus(character, messageOutput);
                    break;
                case "gamecontent":
                    TestGameContentIntegration(character, messageOutput);
                    break;
                case "config":
                    ShowConfig(character, messageOutput);
                    break;
                case "sessions":
                    ShowSessionStats(character, messageOutput);
                    break;
                default:
                    CommandManager.SendErrorText(this, messageOutput, $"未知命令: {command}");
                    ShowHelp(character, messageOutput);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "执行AI聊天测试命令时出错");
            CommandManager.SendErrorText(this, messageOutput, "处理AI聊天测试命令时发生错误。");
        }
    }

    /// <summary>
    /// 显示帮助信息
    /// </summary>
    private void ShowHelp(Character character, IMessageOutput messageOutput)
    {
        CommandManager.SendNormalText(this, messageOutput, GetCommandHelpText());
    }

    /// <summary>
    /// 显示AI聊天系统状态
    /// </summary>
    private void ShowAIChatStatus(Character character, IMessageOutput messageOutput)
    {
        var isEnabled = AIChatManager.Instance.IsAIChatEnabled();
        var sessionCount = AIChatManager.Instance.GetActiveSessionCount();
        
        CommandManager.SendNormalText(this, messageOutput, "=== AI聊天系统状态 ===");
        CommandManager.SendNormalText(this, messageOutput, $"启用状态: {(isEnabled ? "✅ 已启用" : "❌ 已禁用")}");
        CommandManager.SendNormalText(this, messageOutput, $"活跃会话数: {sessionCount}");
        
        if (isEnabled)
        {
            var config = AIChatManager.Instance.GetConfig();
            CommandManager.SendNormalText(this, messageOutput, $"API URL: {config.ApiUrl}");
            CommandManager.SendNormalText(this, messageOutput, $"模型: {config.Model}");
            CommandManager.SendNormalText(this, messageOutput, $"最大令牌数: {config.MaxTokens}");
            CommandManager.SendNormalText(this, messageOutput, $"上下文窗口: {config.ContextWindow}");
        }
    }

    /// <summary>
    /// 测试游戏内容集成功能
    /// </summary>
    private void TestGameContentIntegration(Character character, IMessageOutput messageOutput)
    {
        CommandManager.SendNormalText(this, messageOutput, "=== 游戏内容集成功能测试 ===");
        
        // 测试游戏内容查询功能
        var testQueries = new[]
        {
            "新手任务",
            "武器",
            "NPC",
            "怪物",
            "副本"
        };
        
        foreach (var query in testQueries)
        {
            CommandManager.SendNormalText(this, messageOutput, $"测试查询: {query}");
            
            try
            {
                var result = AIChatManager.Instance.QueryGameContent(query);
                if (!string.IsNullOrEmpty(result))
                {
                    var lines = result.Split('\n').Take(3); // 只显示前3行
                    CommandManager.SendNormalText(this, messageOutput, $"结果: {string.Join(" ", lines)}...");
                }
                else
                {
                    CommandManager.SendNormalText(this, messageOutput, "结果: 无匹配内容");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"测试游戏内容查询时出错: {query}");
                CommandManager.SendErrorText(this, messageOutput, $"查询失败: {ex.Message}");
            }
            
            CommandManager.SendNormalText(this, messageOutput, "---");
        }
        
        // 显示游戏内容集成状态
        var gameContentStatus = AIChatManager.Instance.GetGameContentStatus();
        CommandManager.SendNormalText(this, messageOutput, "=== 游戏内容集成状态 ===");
        foreach (var status in gameContentStatus)
        {
            CommandManager.SendNormalText(this, messageOutput, $"{status.Key}: {status.Value}");
        }
    }

    /// <summary>
    /// 显示当前配置
    /// </summary>
    private void ShowConfig(Character character, IMessageOutput messageOutput)
    {
        var config = AIChatManager.Instance.GetConfig();
        
        CommandManager.SendNormalText(this, messageOutput, "=== AI聊天系统配置 ===");
        CommandManager.SendNormalText(this, messageOutput, $"启用状态: {config.IsEnabled}");
        CommandManager.SendNormalText(this, messageOutput, $"API URL: {config.ApiUrl}");
        CommandManager.SendNormalText(this, messageOutput, $"模型: {config.Model}");
        CommandManager.SendNormalText(this, messageOutput, $"最大令牌数: {config.MaxTokens}");
        CommandManager.SendNormalText(this, messageOutput, $"温度: {config.Temperature}");
        CommandManager.SendNormalText(this, messageOutput, $"上下文窗口: {config.ContextWindow}");
        CommandManager.SendNormalText(this, messageOutput, $"记忆功能: {config.MemoryEnabled}");
        CommandManager.SendNormalText(this, messageOutput, $"情感功能: {config.EmotionEnabled}");
        CommandManager.SendNormalText(this, messageOutput, $"缓存功能: {config.CacheEnabled}");
        CommandManager.SendNormalText(this, messageOutput, $"响应延迟: {config.ResponseDelay}ms");
        CommandManager.SendNormalText(this, messageOutput, $"会话超时: {config.SessionTimeout}分钟");
        
        CommandManager.SendNormalText(this, messageOutput, "=== 搜索功能配置 ===");
        CommandManager.SendNormalText(this, messageOutput, $"搜索功能: {config.SearchEnabled}");
        CommandManager.SendNormalText(this, messageOutput, $"搜索API URL: {config.SearchApiUrl}");
        CommandManager.SendNormalText(this, messageOutput, $"最大搜索结果数: {config.MaxSearchResults}");
        CommandManager.SendNormalText(this, messageOutput, $"搜索超时: {config.SearchTimeout}秒");
    }

    /// <summary>
    /// 显示会话统计
    /// </summary>
    private void ShowSessionStats(Character character, IMessageOutput messageOutput)
    {
        var sessionStats = AIChatManager.Instance.GetSessionStats();
        
        CommandManager.SendNormalText(this, messageOutput, "=== AI聊天会话统计 ===");
        foreach (var stat in sessionStats)
        {
            CommandManager.SendNormalText(this, messageOutput, $"{stat.Key}: {stat.Value}");
        }
    }
}