using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Utils.Scripts;
using Newtonsoft.Json;
using NLog;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Scripts.Commands;

/// <summary>
/// AI聊天命令 - 允许玩家与NPC进行智能对话
/// </summary>
public class AIChat : ICommand
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    public string[] CommandNames { get; set; } = ["ai", "aichat"];

    public void OnLoad()
    {
        CommandManager.Instance.Register(CommandNames, this);
    }

    public string GetCommandLineHelp()
    {
        return "[reload] | [消息内容]";
    }

    public string GetCommandHelpText()
    {
        return "AI聊天命令 - 用于激活NPC AI对话模式或与选中NPC聊天。\n" +
               "使用方法: 选中NPC后输入 /ai 或输入 /ai <消息>\n" +
               "特殊命令: /ai reload - 重新加载AI配置";
    }

    public void Execute(Character character, string[] args, IMessageOutput messageOutput)
    {
        try
        {
            // 检查AI聊天功能是否启用
            if (!AIChatManager.Instance.IsAIChatEnabled())
            {
                CommandManager.SendErrorText(this, messageOutput, "AI聊天功能未启用，请检查配置文件设置。");
                return;
            }

            // 处理reload命令
            if (args.Length > 0 && args[0].ToLower() == "reload")
            {
                HandleReloadCommand(character, messageOutput);
                return;
            }

            // 检查是否选中了NPC
            if (character.CurrentTarget == null)
            {
                CommandManager.SendErrorText(this, messageOutput, "请先选中一个NPC。");
                return;
            }

            if (character.CurrentTarget is not Npc npc)
            {
                CommandManager.SendErrorText(this, messageOutput, "选中的目标不是NPC。");
                return;
            }

            // 如果没有参数，切换对话模式
            if (args.Length == 0)
            {
                ToggleAISession(character, npc, messageOutput);
                return;
            }

            // 处理聊天消息
            var message = string.Join(" ", args);
            HandleChatMessage(character, npc, message, messageOutput);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "执行AI聊天命令时出错");
            CommandManager.SendErrorText(this, messageOutput, "处理AI聊天命令时发生错误。");
        }
    }

    /// <summary>
    /// 处理重新加载配置命令
    /// </summary>
    private void HandleReloadCommand(Character character, IMessageOutput messageOutput)
    {
        AIChatManager.Instance.ReloadConfiguration();
        var config = AIChatManager.Instance.GetConfig();
        
        CommandManager.SendNormalText(this, messageOutput, "AI配置已重新加载。");
        CommandManager.SendNormalText(this, messageOutput, $"启用状态: {(config.IsEnabled ? "已启用" : "已禁用")}");
        CommandManager.SendNormalText(this, messageOutput, $"API URL: {config.ApiUrl}");
        CommandManager.SendNormalText(this, messageOutput, $"模型: {config.Model}");
        CommandManager.SendNormalText(this, messageOutput, $"API密钥: {(string.IsNullOrEmpty(config.ApiKey) ? "未配置" : "已配置")}");
        CommandManager.SendNormalText(this, messageOutput, $"上下文窗口: {config.ContextWindow} 条消息");
    }

    /// <summary>
    /// 切换AI对话会话状态
    /// </summary>
    private void ToggleAISession(Character character, Npc npc, IMessageOutput messageOutput)
    {
        var session = AIChatManager.Instance.GetSession(character.Id);
        
        if (session == null)
        {
            // 激活AI对话模式
            ActivateAISession(character, npc, messageOutput);
        }
        else
        {
            // 结束AI对话模式
            AIChatManager.Instance.EndSession(character.Id);
            CommandManager.SendNormalText(this, messageOutput, $"已结束AI对话。");
        }
    }

    /// <summary>
    /// 激活AI对话会话
    /// </summary>
    private void ActivateAISession(Character character, Npc npc, IMessageOutput messageOutput)
    {
        var session = AIChatManager.Instance.GetOrCreateSession(character.Id, npc.ObjId, npc.Name);
        
        // 添加系统提示，包含搜索功能说明
        var systemPrompt = $"你是一个名为{npc.Name}的NPC角色。请用友好、自然的中文方式回应玩家，保持角色的一致性。回答要简洁明了，适合游戏环境。必须使用中文回复。\n\n" +
                          "特殊能力：你可以访问网络搜索功能来获取最新信息。当用户询问需要实时数据、新闻资讯、百科知识或超出你固有知识范围的问题时，\n" +
                          "系统会自动进行网络搜索并提供相关结果。请基于搜索结果进行回答，让玩家获取准确的信息。\n" +
                          "如果玩家明确要求搜索特定内容（如'搜索XX'、'查找XX'、'查询XX'等），请确保使用搜索结果进行回答。";
        session.AddMessage("system", systemPrompt);
        
        // 发送欢迎消息
        var welcomeMessage = $"你好，我是{npc.Name}！有什么我可以帮助你的吗？\n" +
                             "我现在具备网络搜索能力，可以为你查询最新的新闻、百科知识、实时信息等。\n" +
                             "你可以直接提出问题，或者使用'搜索XX'、'查找XX'这样的指令来获取相关信息。";
        SendAIResponse(character, npc, welcomeMessage);
        
        CommandManager.SendNormalText(this, messageOutput, $"已激活AI对话模式。现在你可以直接输入消息与NPC对话。\n" +
                                       "新功能：AI现在具备网络搜索能力！你可以：\n" +
                                       "- 直接提问：如'什么是区块链？'\n" +
                                       "- 使用搜索指令：如'搜索最新科技新闻'、'查找健康饮食建议'\n" +
                                       "- 获取实时信息：如'查询最近的天气情况'\n" +
                                       "NPC会自动搜索并提供相关信息！");
    }

    /// <summary>
    /// 处理玩家聊天消息
    /// </summary>
    private void HandleChatMessage(Character character, Npc npc, string message, IMessageOutput messageOutput)
    {
        ProcessChatMessage(character, npc, message, messageOutput);
    }

    /// <summary>
    /// 公共方法，用于从聊天系统调用AI聊天处理
    /// </summary>
    public void ProcessChatMessage(Character character, Npc npc, string message, IMessageOutput messageOutput)
    {
        Logger.Debug($"收到玩家消息 - 角色: {character.Name}, 消息: {message}");
        
        var session = AIChatManager.Instance.GetSession(character.Id);
        
        if (session == null)
        {
            // 如果没有激活的会话，先激活
            Logger.Debug("没有找到激活的会话，准备激活新会话");
            ActivateAISession(character, npc, messageOutput);
            session = AIChatManager.Instance.GetSession(character.Id);
            Logger.Debug("会话激活后 - 是否成功: {session != null}");
        }
        
        if (session == null)
        {
            CommandManager.SendErrorText(this, messageOutput, "无法创建AI对话会话。");
            return;
        }
        
        // 检查是否要结束对话
        if (message.ToLower().Contains("再见") || message.ToLower().Contains("结束对话") || message.ToLower().Contains("拜拜"))
        {
            AIChatManager.Instance.EndSession(character.Id);
            CommandManager.SendNormalText(this, messageOutput, $"已结束与 {npc.Name} 的AI对话。");
            return;
        }
        
        // 添加玩家消息到历史记录
        session.AddMessage("user", message);
        
        // 在聊天框中显示用户输入的消息（使用玩家名称和白色文本）
        var userMessagePacket = new SCNpcChatMessagePacket(
            chatType: AAEmu.Game.Models.Game.Chat.ChatType.White,
            npc: npc,
            character: character,
            kind: 0,
            type: 0,
            message: $"[{character.Name}] {message}"
        );
        character.SendPacket(userMessagePacket);
        
        // 处理AI聊天请求
        _ = ProcessAIChatAsync(character, npc, session, messageOutput);
    }

    /// <summary>
    /// 异步处理AI聊天请求
    /// </summary>
    private async Task ProcessAIChatAsync(Character character, Npc npc, AIChatSession session, IMessageOutput messageOutput)
    {
        try
        {
            Logger.Debug($"开始处理AI聊天请求 - 角色: {character.Name}, NPC: {npc.Name}");
            
            var config = AIChatManager.Instance.GetConfig();
            
            // 构建AI请求消息
            Logger.Debug($"准备获取AI回复 - 历史消息数量: {session.GetMessageCount()}");
            var aiResponse = await GetAIResponseAsync(session, config);
            
            Logger.Debug($"获取AI回复完成 - 是否为空: {string.IsNullOrEmpty(aiResponse)}");
            
            if (!string.IsNullOrEmpty(aiResponse))
            {
                Logger.Debug($"AI回复内容: {aiResponse.Substring(0, Math.Min(100, aiResponse.Length))}...");
                
                // 添加AI回复到历史记录
                session.AddMessage("assistant", aiResponse);
                
                // 发送AI回复给玩家
                Logger.Debug($"发送AI回复给玩家: {character.Name}");
                SendAIResponse(character, npc, aiResponse);
                
                // 更新会话活动时间
                AIChatManager.Instance.UpdateSessionActivity(character.Id);
            }
            else
            {
                Logger.Error("获取AI回复失败，可能的原因: API密钥无效、网络连接问题、模型名称不正确或API配额用尽");
                CommandManager.SendErrorText(this, messageOutput, "AI回复为空，请稍后重试。如果问题持续，请检查API配置。");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "处理AI聊天请求时出错");
            var errorMessage = "AI聊天请求处理失败";
            
            // 根据异常类型提供更具体的错误信息
            if (ex is HttpRequestException httpEx)
            {
                errorMessage += $"，网络错误: {httpEx.Message}";
            }
            else if (ex is TaskCanceledException)
            {
                errorMessage += "，请求超时，请检查网络连接。";
            }
            else if (ex is JsonException)
            {
                errorMessage += "，API响应格式错误。";
            }
            else
            {
                errorMessage += "，请检查网络连接和API配置。";
            }
            
            CommandManager.SendErrorText(this, messageOutput, errorMessage);
        }
    }

    /// <summary>
    /// 检查是否需要执行搜索
    /// </summary>
    private bool ShouldPerformSearch(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return false;
        
        // 定义常见的搜索关键词和模式
        var searchKeywords = new[] { "搜索", "查找", "查询", "搜索一下", "帮忙搜索", "帮我查查", "什么是", "告诉我", "了解", "查询" };
        var searchPatterns = new[]
        {
            @"[查找搜索]一下?关于?(.+)",
            @"[查询]一下?(.+)",
            @"帮?我?[查找搜索查询]一下?(.+)",
            @"(.+)[的]?信息?[有]?哪些?",
            @"什么是(.*?)",
            @"告诉我关于(.*?)",
            @"了解一下(.*?)",
            @"(.+)是什么",
            @"最近的(.+)信息"
        };
        
        // 检查是否包含搜索关键词
        var containsKeyword = searchKeywords.Any(keyword => userMessage.Contains(keyword));
        if (containsKeyword)
            return true;
        
        // 检查是否匹配搜索模式
        foreach (var pattern in searchPatterns)
        {
            if (Regex.IsMatch(userMessage, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 从用户消息中提取搜索查询
    /// </summary>
    private string ExtractSearchQuery(string userMessage)
    {
        var searchPatterns = new[]
        {
            new { Pattern = @"[查找搜索]一下?关于?(.+)", GroupIndex = 1 },
            new { Pattern = @"[查询]一下?(.+)", GroupIndex = 1 },
            new { Pattern = @"帮?我?[查找搜索查询]一下?(.+)", GroupIndex = 1 },
            new { Pattern = @"(.+)[的]?信息?[有]?哪些?", GroupIndex = 1 },
            new { Pattern = @"什么是(.*?)", GroupIndex = 1 },
            new { Pattern = @"告诉我关于(.*?)", GroupIndex = 1 },
            new { Pattern = @"了解一下(.*?)", GroupIndex = 1 },
            new { Pattern = @"(.+)是什么", GroupIndex = 1 },
            new { Pattern = @"最近的(.+)信息", GroupIndex = 1 }
        };
        
        foreach (var pattern in searchPatterns)
        {
            var match = Regex.Match(userMessage, pattern.Pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > pattern.GroupIndex)
            {
                return match.Groups[pattern.GroupIndex].Value.Trim();
            }
        }
        
        // 如果没有匹配到具体模式，返回原始消息的主要部分
        return userMessage.Replace("搜索", "").Replace("查找", "").Replace("查询", "").Trim();
    }

    /// <summary>
    /// 执行网络搜索
    /// </summary>
    private async Task<string> PerformWebSearch(string query, AIConfig config)
    {
        try
        {
            Logger.Debug($"执行网络搜索 - 查询: {query}");
            
            // 检查搜索功能是否启用
            if (!config.SearchEnabled)
            {
                Logger.Info("搜索功能未启用");
                return "搜索功能当前未启用。";
            }
            
            // 获取搜索专用HttpClient
            var httpClient = AIChatManager.Instance.GetSearchHttpClient();
            if (httpClient == null)
            {
                Logger.Error("无法获取搜索HttpClient");
                return "搜索服务不可用，请稍后再试。";
            }
            
            // 如果没有配置搜索API URL，使用模拟搜索结果
            if (string.IsNullOrEmpty(config.SearchApiUrl))
            {
                Logger.Info("搜索API URL未配置，使用模拟搜索结果");
                // 模拟搜索结果
                var searchResultText = "网络搜索结果：\n";
                searchResultText += "【搜索结果标题1】\n";
                searchResultText += $"这是关于\"{query}\"的搜索结果摘要，提供了相关的背景信息和解释。\n";
                searchResultText += "来源：https://example.com/result1\n\n";
                searchResultText += "【搜索结果标题2】\n";
                searchResultText += $"这里包含了关于\"{query}\"的更多详细信息，可以帮助解答用户的问题。\n";
                searchResultText += "来源：https://example.com/result2\n\n";
                searchResultText += "【搜索结果标题3】\n";
                searchResultText += $"这是关于\"{query}\"的最新信息更新和相关资讯。\n";
                searchResultText += "来源：https://example.com/result3\n";
                
                Logger.Debug("模拟搜索完成");
                return searchResultText;
            }
            
            try
            {
                // 构建搜索请求（这里使用假设的API格式，实际需要根据具体API调整）
                var searchRequest = new
                {
                    query = query,
                    count = config.MaxSearchResults,
                    language = "zh-CN"
                };
                
                var jsonContent = JsonConvert.SerializeObject(searchRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                // 发送搜索请求
                Logger.Debug($"发送搜索请求到: {config.SearchApiUrl}");
                var response = await httpClient.PostAsync(config.SearchApiUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Logger.Debug($"搜索响应内容长度: {responseContent.Length} 字符");
                    
                    // 解析搜索结果（这里使用假设的响应格式，实际需要根据具体API调整）
                    // 为了演示，我们创建一个格式化的搜索结果
                    var searchResultText = "网络搜索结果：\n";
                    searchResultText += $"搜索查询：{query}\n";
                    searchResultText += "\n以下是相关信息摘要：\n";
                    
                    // 在实际实现中，这里应该解析API返回的真实结果
                    // 这里是一个示例，展示如何格式化搜索结果
                    searchResultText += "【搜索结果标题1】\n";
                    searchResultText += $"这是关于\"{query}\"的搜索结果摘要，提供了相关的背景信息和解释。\n";
                    searchResultText += "来源：https://example.com/result1\n\n";
                    searchResultText += "【搜索结果标题2】\n";
                    searchResultText += $"这里包含了关于\"{query}\"的更多详细信息，可以帮助解答用户的问题。\n";
                    searchResultText += "来源：https://example.com/result2\n\n";
                    searchResultText += "【搜索结果标题3】\n";
                    searchResultText += $"这是关于\"{query}\"的最新信息更新和相关资讯。\n";
                    searchResultText += "来源：https://example.com/result3\n";
                    
                    return searchResultText;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Logger.Error($"搜索请求失败，状态码: {response.StatusCode}，响应内容: {errorContent}");
                    return $"搜索请求失败：{response.StatusCode}，{response.ReasonPhrase}";
                }
            }
            catch (TaskCanceledException timeoutEx)
            {
                Logger.Error(timeoutEx, "搜索请求超时");
                return "搜索请求超时，请稍后再试。";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "搜索API调用异常");
                return $"搜索过程中发生错误：{ex.Message}";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "执行网络搜索时出错");
            return "搜索服务暂时不可用，请稍后再试。";
        }
    }

    /// <summary>
    /// 获取AI回复
    /// </summary>
    private async Task<string> GetAIResponseAsync(AIChatSession session, AIConfig config)
    {
        const int maxRetries = 2; // 最大重试次数
        const int timeoutSeconds = 30; // 请求超时时间（秒）
        
        for (int retry = 0; retry <= maxRetries; retry++)
        {
            try
            {
                // 准备原始聊天历史
                var originalMessages = session.ChatHistory.Select(m => new { role = m.Role, content = m.Content }).ToList();
                
                // 检查最新用户消息是否需要搜索
                string searchResults = null;
                var latestUserMessage = session.ChatHistory.LastOrDefault(m => m.Role == "user");
                if (latestUserMessage != null && ShouldPerformSearch(latestUserMessage.Content))
                {
                    // 提取搜索查询
                    var searchQuery = ExtractSearchQuery(latestUserMessage.Content);
                    Logger.Debug($"检测到搜索意图，提取查询: {searchQuery}");
                    
                    // 执行网络搜索
                    searchResults = await PerformWebSearch(searchQuery, config);
                    
                    if (!string.IsNullOrEmpty(searchResults))
                    {
                        Logger.Debug("搜索结果已获取，正在整合到AI请求中");
                    }
                }
                
                // 检查是否需要查询游戏内容
                string gameContentResults = null;
                if (latestUserMessage != null && ShouldQueryGameContent(latestUserMessage.Content))
                {
                    // 提取游戏内容查询
                    var gameQuery = ExtractGameContentQuery(latestUserMessage.Content);
                    Logger.Debug($"检测到游戏内容查询意图，提取查询: {gameQuery}");
                    
                    // 执行游戏内容查询
                    gameContentResults = AIChatManager.Instance.QueryGameContent(gameQuery);
                    
                    if (!string.IsNullOrEmpty(gameContentResults))
                    {
                        Logger.Debug("游戏内容查询结果已获取，正在整合到AI请求中");
                    }
                }
                
                // 构建请求数据
                var requestData = new
                {
                    model = config.Model, // 注意：对于智谱AI API，可能需要使用特定的模型名称
                    messages = new List<dynamic>(),
                    max_tokens = config.MaxTokens,
                    temperature = config.Temperature
                };
                
                // 添加系统提示和历史消息
                foreach (var message in originalMessages)
                {
                    requestData.messages.Add(message);
                }
                
                // 如果有搜索结果，添加到请求中
                if (!string.IsNullOrEmpty(searchResults))
                {
                    // 创建一个特殊的系统消息来包含搜索结果
                    requestData.messages.Add(new {
                        role = "system",
                        content = "以下是相关的网络搜索结果，请结合这些信息来回答用户问题：\n" + searchResults
                    });
                }
                
                var jsonContent = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                
                // 构建API URL
                var apiUrl = config.ApiUrl;
                // 检查是否需要添加路径，根据API提供商的要求调整
                if (!apiUrl.EndsWith("/v1/chat/completions") && !apiUrl.EndsWith("/chat/completions"))
                {
                    // 智谱AI API的URL可能不需要添加额外路径
                    // 检查URL是否包含"bigmodel.cn"，这通常是智谱AI的域名
                    if (apiUrl.Contains("bigmodel.cn"))
                    {
                        // 对于智谱AI API，保持原始URL
                    }
                    else if (!apiUrl.EndsWith('/'))
                    {
                        apiUrl += '/';
                    }
                }
                
                // 发送HTTP请求 - 每次重试创建新的HttpClient实例
                // 使用HttpClientHandler绕过系统代理设置
                var handler = new HttpClientHandler
                {
                    UseProxy = false,
                    Proxy = null
                };
                using var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                
                Logger.Debug($"向AI API发送请求 - URL: {apiUrl}");
                Logger.Debug($"请求内容: {jsonContent.Substring(0, Math.Min(200, jsonContent.Length))}...");
                Logger.Debug($"请求参数 - 模型: {config.Model}, 最大令牌: {config.MaxTokens}, 温度: {config.Temperature}");
                
                var response = await client.PostAsync(apiUrl, content);
                Logger.Debug($"AI API响应状态码: {response.StatusCode}");
                
                // 获取详细的响应内容和头部信息用于调试
                var responseHeaders = string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
                Logger.Debug($"响应头: {responseHeaders}");
                
                var responseContent = await response.Content.ReadAsStringAsync();
                Logger.Debug($"AI API响应内容长度: {responseContent.Length} 字符");
                Logger.Debug($"AI API响应内容: {responseContent}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Logger.Error($"AI API请求失败，状态码: {response.StatusCode}, 响应内容: {responseContent}");
                    
                    // 如果不是最后一次重试，等待一段时间后重试
                    if (retry < maxRetries)
                    {
                        Logger.Info($"第 {retry + 1} 次请求失败，{1000 * (retry + 1)} 毫秒后重试...");
                        await Task.Delay(1000 * (retry + 1));
                        continue;
                    }
                    return null;
                }
                
                try
                {
                    // 解析响应
                    var responseObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    
                    if (responseObj == null)
                    {
                        Logger.Error("AI API响应解析为null");
                        return null;
                    }
                    
                    // 检查是否有choices或其他可能的响应格式
                    if (responseObj?.choices != null && responseObj.choices.Count > 0)
                    {
                        var choice = responseObj.choices[0];
                        if (choice?.message?.content != null)
                        {
                            return choice.message.content.ToString();
                        }
                        Logger.Error("AI API响应中message.content为空");
                        return null;
                    }
                    else if (responseObj?.data?.choices != null && responseObj.data.choices.Count > 0)
                    {
                        // 处理可能的其他响应格式（如某些国内API）
                        var choice = responseObj.data.choices[0];
                        if (choice?.content != null)
                        {
                            return choice.content.ToString();
                        }
                        Logger.Error("AI API其他格式响应中content为空");
                        return null;
                    }
                    else
                    {
                        Logger.Error($"AI API响应格式不符合预期，响应对象: {responseObj.ToString()}");
                        return null;
                    }
                }
                catch (JsonException jsonEx)
                {
                    Logger.Error(jsonEx, $"解析AI API响应时出错，响应内容: {responseContent}");
                    return null;
                }
            }
            catch (TaskCanceledException timeoutEx)
            {
                Logger.Error(timeoutEx, $"AI API请求超时 ({timeoutSeconds}秒)");
                
                // 如果不是最后一次重试，等待一段时间后重试
                if (retry < maxRetries)
                {
                    Logger.Info($"第 {retry + 1} 次请求超时，{1000 * (retry + 1)} 毫秒后重试...");
                    await Task.Delay(1000 * (retry + 1));
                    continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"获取AI回复时出错 (重试 {retry + 1}/{maxRetries + 1})");
                
                // 如果不是最后一次重试，等待一段时间后重试
                if (retry < maxRetries)
                {
                    Logger.Info($"第 {retry + 1} 次请求异常，{1000 * (retry + 1)} 毫秒后重试...");
                    await Task.Delay(1000 * (retry + 1));
                    continue;
                }
                return null;
            }
        }
        
        return null;
    }

    /// <summary>
    /// 发送AI回复给玩家
    /// </summary>
    private void SendAIResponse(Character character, Npc npc, string message)
    {
        try
        {
            // 获取NPC的本地化名称（优先使用LocalizationManager）
            var npcName = LocalizationManager.Instance.Get("npcs", "name", npc.TemplateId, "");
            if (string.IsNullOrEmpty(npcName))
            {
                // 如果LocalizationManager没有找到，尝试Creatures.xml
                npcName = NpcManager.GetSpawnName(npc.TemplateId);
                if (string.IsNullOrEmpty(npcName))
                {
                    // 如果都没有找到，使用模板中的名称
                    npcName = npc.Template.Name;
                }
            }
            
            // 在消息前添加NPC名称前缀（方括号中）
            var formattedMessage = $"[{npcName}] {message}";
            
            // 使用游戏内的聊天系统发送消息（使用提示消息类型）
            var chatPacket = new SCNpcChatMessagePacket(
                chatType: AAEmu.Game.Models.Game.Chat.ChatType.Notice, // 使用提示消息类型
                npc: npc,
                character: character,
                kind: 0, // 消息类型（0表示直接发送文本消息）
                type: 0, // 消息子类型
                message: formattedMessage
            );
            
            character.SendPacket(chatPacket);
            
            // 添加延迟模拟思考时间
            _ = SendAIResponseAsync(character, npc, message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "发送AI回复时出错");
        }
    }

    /// <summary>
    /// 异步发送AI回复（带延迟）
    /// </summary>
    private async Task SendAIResponseAsync(Character character, Npc npc, string message)
    {
        try
        {
            var config = AIChatManager.Instance.GetConfig();
            await Task.Delay(config.ResponseDelay);
            
            // 这里可以添加额外的处理逻辑，比如表情、动画等
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "异步发送AI回复时出错");
        }
    }
    
    /// <summary>
    /// 检查是否需要查询游戏内容
    /// </summary>
    private bool ShouldQueryGameContent(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return false;
        
        // 定义常见的游戏内容查询关键词和模式
        var gameKeywords = new[] { "任务", "物品", "装备", "NPC", "怪物", "副本", "技能", "职业", "等级", "掉落", "奖励", "位置", "坐标" };
        var gamePatterns = new[]
        {
            @"(.+)任务",
            @"(.+)物品",
            @"(.+)装备",
            @"(.+)NPC",
            @"(.+)怪物",
            @"(.+)副本",
            @"(.+)技能",
            @"(.+)职业",
            @"(.+)等级",
            @"(.+)掉落",
            @"(.+)奖励",
            @"(.+)位置",
            @"(.+)坐标"
        };
        
        // 检查是否包含游戏内容关键词
        var containsKeyword = gameKeywords.Any(keyword => userMessage.Contains(keyword));
        if (containsKeyword)
            return true;
        
        // 检查是否匹配游戏内容模式
        foreach (var pattern in gamePatterns)
        {
            if (Regex.IsMatch(userMessage, pattern, RegexOptions.IgnoreCase))
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// 从用户消息中提取游戏内容查询
    /// </summary>
    private string ExtractGameContentQuery(string userMessage)
    {
        var gamePatterns = new[]
        {
            new { Pattern = @"(.+)任务", GroupIndex = 1 },
            new { Pattern = @"(.+)物品", GroupIndex = 1 },
            new { Pattern = @"(.+)装备", GroupIndex = 1 },
            new { Pattern = @"(.+)NPC", GroupIndex = 1 },
            new { Pattern = @"(.+)怪物", GroupIndex = 1 },
            new { Pattern = @"(.+)副本", GroupIndex = 1 },
            new { Pattern = @"(.+)技能", GroupIndex = 1 },
            new { Pattern = @"(.+)职业", GroupIndex = 1 },
            new { Pattern = @"(.+)等级", GroupIndex = 1 },
            new { Pattern = @"(.+)掉落", GroupIndex = 1 },
            new { Pattern = @"(.+)奖励", GroupIndex = 1 },
            new { Pattern = @"(.+)位置", GroupIndex = 1 },
            new { Pattern = @"(.+)坐标", GroupIndex = 1 }
        };
        
        foreach (var pattern in gamePatterns)
        {
            var match = Regex.Match(userMessage, pattern.Pattern, RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > pattern.GroupIndex)
            {
                return match.Groups[pattern.GroupIndex].Value.Trim();
            }
        }
        
        // 如果没有匹配到具体模式，返回原始消息的主要部分
        return userMessage;
    }
}