using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers;
using Newtonsoft.Json;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// AI聊天管理器 - 管理AI聊天会话和配置
/// </summary>
public class AIChatManager : Singleton<AIChatManager>, IDisposable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    // AI配置
    private string _apiKey = "";
    private string _apiUrl = "";
    private string _model = "";
    private int _maxTokens = 0;
    private double _temperature = 0.0;
    private bool _isEnabled = false;
    private HttpClient _httpClient;
    private HttpClient _searchHttpClient;
    private int _requestTimeout = 60; // 默认请求超时时间（秒）
    
    // 高级配置
    private int _contextWindow = 0;
    private bool _memoryEnabled = false;
    private bool _emotionEnabled = false;
    private bool _cacheEnabled = false;
    private int _responseDelay = 0;
    private int _sessionTimeout = 0;
    
    // 搜索相关配置
    private bool _searchEnabled = true;
    private string _searchApiUrl = "";
    private string _searchApiKey = "";
    private int _maxSearchResults = 3;
    private int _searchTimeout = 15;
    
    // 智能上下文配置
    private bool _smartContextEnabled = true;
    private int _maxTopicKeywords = 5;
    private int _topicRelevanceThreshold = 1;
    private int _importantMessageLength = 100;
    private bool _enableTopicTracking = true;
    private bool _enableMemoryCompression = true;
    
    // 游戏内容集成配置
    private bool _gameContentIntegrationEnabled = true;
    private int _maxGameResults = 5;
    private bool _enableQuestInfo = true;
    private bool _enableItemInfo = true;
    private bool _enableNpcInfo = true;
    
    // 存储激活的AI聊天会话
    private readonly Dictionary<uint, AIChatSession> _activeSessions = new();
    
    public void Initialize()
    {
        _httpClient = new HttpClient();
        LoadConfiguration();
        Logger.Info($"AI聊天管理器已初始化，状态: {(_isEnabled ? "启用" : "禁用")}");
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
        _searchHttpClient?.Dispose();
    }
    
    private void LoadConfiguration()
    {
        try
        {
            // 从apikey.json文件加载AI配置
            var configPath = Path.Combine(AppContext.BaseDirectory, "Configurations", "apikey.json");
            
            Logger.Info($"尝试加载AI配置文件: {configPath}");
            
            if (!File.Exists(configPath))
            {
                Logger.Warn($"AI配置文件不存在: {configPath}");
                _isEnabled = false;
                _apiKey = "";
                return;
            }
            
            var jsonContent = File.ReadAllText(configPath);
            Logger.Debug($"配置文件内容长度: {jsonContent.Length} 字符");
            
            // 简单检查JSON格式是否有基本的开始和结束花括号
            if (!jsonContent.Trim().StartsWith("{") || !jsonContent.Trim().EndsWith("}"))
            {
                Logger.Error("配置文件JSON格式不正确：缺少开始或结束的花括号");
                Logger.Error($"文件开头: {jsonContent.Trim().Substring(0, Math.Min(20, jsonContent.Length))}");
                Logger.Error($"文件结尾: {jsonContent.Trim().Substring(Math.Max(0, jsonContent.Length - 20), Math.Min(20, jsonContent.Length))}");
                _isEnabled = false;
                _apiKey = "";
                return;
            }
            
            try
            {
                var config = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                
                if (config == null)
                {
                    Logger.Error("配置文件解析为null");
                    _isEnabled = false;
                    _apiKey = "";
                    return;
                }
                
                if (config?.aiChat != null)
                {
                    // 重置所有配置，确保没有旧值残留
                _isEnabled = false;
                _apiKey = "";
                _apiUrl = "";
                _model = "";
                _maxTokens = 0;
                _temperature = 0.0;
                _contextWindow = 0;
                _memoryEnabled = false;
                _emotionEnabled = false;
                _cacheEnabled = false;
                _responseDelay = 0;
                _sessionTimeout = 0;
                _searchEnabled = true;
                _searchApiUrl = "";
                _searchApiKey = "";
                _maxSearchResults = 3;
                _searchTimeout = 15;
                    
                    // 加载基本配置
                    _isEnabled = config.aiChat.enabled;
                    _apiKey = config.aiChat.apiKey?.ToString() ?? "";
                    _apiUrl = config.aiChat.apiUrl?.ToString() ?? "";
                    _model = config.aiChat.model?.ToString() ?? "";
                    _maxTokens = (int)(config.aiChat.maxTokens ?? 0);
                    _temperature = (double)(config.aiChat.temperature ?? 0.0);
                    
                    // 加载高级配置
                    if (config.aiChat.advanced != null)
                    {
                        _contextWindow = (int)(config.aiChat.advanced.contextWindow ?? 0);
                        _memoryEnabled = config.aiChat.advanced.memoryEnabled;
                        _emotionEnabled = config.aiChat.advanced.emotionEnabled;
                        _cacheEnabled = config.aiChat.advanced.cacheEnabled;
                        _responseDelay = (int)(config.aiChat.advanced.responseDelay ?? 0);
                        _sessionTimeout = (int)(config.aiChat.advanced.sessionTimeout ?? 0);
                        
                        // 支持新的请求超时配置
                    if (config.aiChat.advanced.requestTimeout != null)
                    {
                        _requestTimeout = (int)config.aiChat.advanced.requestTimeout;
                        Logger.Debug($"加载请求超时配置: {_requestTimeout}秒");
                        
                        // 重新创建HttpClient实例以应用新的超时设置
                        _httpClient?.Dispose();
                        _httpClient = null;
                    }
                    
                    // 加载搜索相关配置
                    if (config.aiChat.advanced.search != null)
                    {
                        _searchEnabled = config.aiChat.advanced.search.searchEnabled;
                        _searchApiUrl = config.aiChat.advanced.search.searchApiUrl?.ToString() ?? "";
                        _searchApiKey = config.aiChat.advanced.search.searchApiKey?.ToString() ?? "";
                        _maxSearchResults = (int)(config.aiChat.advanced.search.maxSearchResults ?? 3);
                        _searchTimeout = (int)(config.aiChat.advanced.search.searchTimeout ?? 15);
                        
                        Logger.Debug($"加载搜索配置 - 启用: {_searchEnabled}, 搜索API URL: {_searchApiUrl}, 最大结果数: {_maxSearchResults}");
                    }
                    
                    // 加载智能上下文配置
                    if (config.aiChat.advanced.smartContext != null)
                    {
                        _smartContextEnabled = config.aiChat.advanced.smartContext.enabled;
                        _maxTopicKeywords = (int)(config.aiChat.advanced.smartContext.maxTopicKeywords ?? 5);
                        _topicRelevanceThreshold = (int)(config.aiChat.advanced.smartContext.topicRelevanceThreshold ?? 1);
                        _importantMessageLength = (int)(config.aiChat.advanced.smartContext.importantMessageLength ?? 100);
                        _enableTopicTracking = config.aiChat.advanced.smartContext.enableTopicTracking;
                        _enableMemoryCompression = config.aiChat.advanced.smartContext.enableMemoryCompression;
                        
                        Logger.Debug($"加载智能上下文配置 - 启用: {_smartContextEnabled}, 最大关键词: {_maxTopicKeywords}, 话题追踪: {_enableTopicTracking}");
                    }
                    
                    // 加载游戏内容集成配置
                    if (config.aiChat.advanced.gameContent != null)
                    {
                        _gameContentIntegrationEnabled = config.aiChat.advanced.gameContent.enabled;
                        _maxGameResults = (int)(config.aiChat.advanced.gameContent.maxGameResults ?? 5);
                        _enableQuestInfo = config.aiChat.advanced.gameContent.enableQuestInfo;
                        _enableItemInfo = config.aiChat.advanced.gameContent.enableItemInfo;
                        _enableNpcInfo = config.aiChat.advanced.gameContent.enableNpcInfo;
                        
                        Logger.Debug($"加载游戏内容集成配置 - 启用: {_gameContentIntegrationEnabled}, 最大结果数: {_maxGameResults}, 任务信息: {_enableQuestInfo}, 物品信息: {_enableItemInfo}, NPC信息: {_enableNpcInfo}");
                    }
                    }
                    
                    Logger.Debug($"解析的配置: 启用={_isEnabled}, API密钥长度={_apiKey?.Length ?? 0}, URL={_apiUrl}, 模型={_model}");
                    Logger.Debug($"高级配置: 上下文窗口={_contextWindow}, 记忆={_memoryEnabled}, 情感={_emotionEnabled}, 缓存={_cacheEnabled}");
                    
                    // 严格检查配置完整性
                    var hasValidConfig = _isEnabled && 
                                        !string.IsNullOrEmpty(_apiKey) && 
                                        !string.IsNullOrEmpty(_apiUrl) && 
                                        !string.IsNullOrEmpty(_model) && 
                                        _maxTokens > 0;
                    
                    if (hasValidConfig)
                    {
                        Logger.Info($"AI聊天配置加载成功 - URL: {_apiUrl}, 模型: {_model}, 最大令牌: {_maxTokens}");
                    }
                    else
                    {
                        Logger.Warn("AI聊天功能配置不完整或无效，请检查Configurations\\apikey.json文件中的配置");
                        Logger.Warn($"当前配置 - 启用: {_isEnabled}, API密钥: {(string.IsNullOrEmpty(_apiKey) ? "空" : "有")}, URL: {(string.IsNullOrEmpty(_apiUrl) ? "空" : "有")}, 模型: {(string.IsNullOrEmpty(_model) ? "空" : "有")}, 最大令牌: {_maxTokens}");
                        
                        // 如果配置不完整，禁用AI聊天功能
                        _isEnabled = false;
                    }
                }
                else
                {
                    Logger.Warn("AI配置文件格式错误，缺少aiChat节点");
                    _isEnabled = false;
                    _apiKey = "";
                }
            }
            catch (JsonException jsonEx)
            {
                Logger.Error(jsonEx, "配置文件JSON解析错误");
                Logger.Error($"错误信息: {jsonEx.Message}");
                Logger.Error($"错误详情: {jsonEx}");
                _isEnabled = false;
                _apiKey = "";
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "加载AI配置时出错");
            _isEnabled = false;
            _apiKey = "";
        }
        
        Logger.Info($"AI配置加载完成 - 启用状态: {_isEnabled}, API密钥配置: {(string.IsNullOrEmpty(_apiKey) ? "未配置" : "已配置")}");
    }
    
    /// <summary>
    /// 获取AI配置
    /// </summary>
    public AIConfig GetConfig()
        {
            return new AIConfig
            {
                IsEnabled = _isEnabled,
                ApiUrl = _apiUrl,
                Model = _model,
                ApiKey = _apiKey,
                MaxTokens = _maxTokens,
                Temperature = _temperature,
                ContextWindow = _contextWindow,
                MemoryEnabled = _memoryEnabled,
                EmotionEnabled = _emotionEnabled,
                CacheEnabled = _cacheEnabled,
                ResponseDelay = _responseDelay,
                SessionTimeout = _sessionTimeout,
                SearchEnabled = _searchEnabled,
                SearchApiUrl = _searchApiUrl,
                SearchApiKey = _searchApiKey,
                MaxSearchResults = _maxSearchResults,
                SearchTimeout = _searchTimeout
            };
        }
    
    /// <summary>
    /// 重新加载AI配置（用于热更新配置）
    /// </summary>
    public void ReloadConfiguration()
    {
        Logger.Info("重新加载AI配置...");
        LoadConfiguration();
        Logger.Info($"AI配置重新加载完成，状态: {(_isEnabled ? "启用" : "禁用")}");
    }
    
    /// <summary>
    /// 检查是否启用了AI聊天
    /// </summary>
    public bool IsAIChatEnabled()
    {
        return _isEnabled;
    }
    
    /// <summary>
    /// 获取或创建AI聊天会话
    /// </summary>
    public AIChatSession GetOrCreateSession(uint characterId, uint npcId, string npcName)
    {
        if (_activeSessions.TryGetValue(characterId, out var existingSession))
        {
            // 如果切换到不同的NPC，结束旧会话
            if (existingSession.NpcId != npcId)
            {
                EndSession(characterId);
            }
            else
            {
                existingSession.LastActivity = DateTime.UtcNow;
                return existingSession;
            }
        }
        
        // 创建新会话
        var newSession = new AIChatSession
        {
            CharacterId = characterId,
            NpcId = npcId,
            NpcName = npcName,
            ChatHistory = new List<ChatMessage>(),
            StartTime = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            ContextWindow = _contextWindow
        };
        
        _activeSessions[characterId] = newSession;
        Logger.Info($"创建新的AI聊天会话 - 角色ID: {characterId}, NPC: {npcName}");
        return newSession;
    }
    
    /// <summary>
    /// 获取指定角色的AI聊天会话
    /// </summary>
    public AIChatSession GetSession(uint characterId)
    {
        return _activeSessions.TryGetValue(characterId, out var session) ? session : null;
    }
    
    /// <summary>
    /// 结束AI聊天会话
    /// </summary>
    public void EndSession(uint characterId)
    {
        if (_activeSessions.Remove(characterId, out var session))
        {
            Logger.Info($"结束AI聊天会话 - 角色ID: {characterId}, NPC: {session.NpcName}");
        }
    }
    
    /// <summary>
    /// 更新会话的最后活动时间
    /// </summary>
    public void UpdateSessionActivity(uint characterId)
    {
        if (_activeSessions.TryGetValue(characterId, out var session))
        {
            session.LastActivity = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 清理过期的会话（超过配置时间无活动的会话）
    /// </summary>
    public void CleanupExpiredSessions()
    {
        var expiredSessions = _activeSessions
            .Where(kvp => DateTime.UtcNow - kvp.Value.LastActivity > TimeSpan.FromMinutes(_sessionTimeout))
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var characterId in expiredSessions)
        {
            EndSession(characterId);
        }
        
        if (expiredSessions.Count > 0)
        {
            Logger.Info($"清理了 {expiredSessions.Count} 个过期的AI聊天会话");
        }
    }
    
    /// <summary>
    /// 获取所有激活的会话数量
    /// </summary>
    public int GetActiveSessionCount()
    {
        return _activeSessions.Count;
    }
    
    /// <summary>
    /// 获取会话统计信息
    /// </summary>
    public Dictionary<string, object> GetSessionStats()
    {
        return new Dictionary<string, object>
        {
            ["activeSessions"] = _activeSessions.Count,
            ["isEnabled"] = _isEnabled,
            ["apiUrl"] = _apiUrl,
            ["model"] = _model,
            ["hasApiKey"] = !string.IsNullOrEmpty(_apiKey),
            ["contextWindow"] = _contextWindow,
            ["memoryEnabled"] = _memoryEnabled,
            ["sessionTimeout"] = _sessionTimeout
        };
    }
    
    /// <summary>
    /// 智能压缩上下文，保留关键信息
    /// </summary>
    public string CompressContext(List<ChatMessage> history, int maxMessages = 10)
    {
        if (history == null || history.Count <= maxMessages)
            return string.Join("\n", history?.Select(m => $"{m.Role}: {m.Content}") ?? new List<string>());
        
        // 根据配置决定是否使用智能上下文压缩
        if (_smartContextEnabled && _enableMemoryCompression)
        {
            var compressed = SmartContextCompression(history, maxMessages);
            return string.Join("\n", compressed.Select(m => $"{m.Role}: {m.Content}"));
        }
        else
        {
            // 使用简单的最近消息截取
            var compressed = history.TakeLast(maxMessages).ToList();
            return string.Join("\n", compressed.Select(m => $"{m.Role}: {m.Content}"));
        }
    }
    
    /// <summary>
    /// 智能上下文压缩算法
    /// </summary>
    private List<ChatMessage> SmartContextCompression(List<ChatMessage> history, int maxMessages)
    {
        if (history.Count <= maxMessages)
            return history.ToList();
        
        var result = new List<ChatMessage>();
        
        // 1. 保留系统消息和重要信息
        var importantMessages = history.Where(m => 
            m.Role == "system" || 
            m.Content.Contains("重要") ||
            m.Content.Contains("关键") ||
            m.Content.Contains("记住") ||
            m.Content.Length > 100 ||
            IsImportantMessage(m.Content)
        ).Take(2).ToList();
        
        result.AddRange(importantMessages);
        
        // 2. 识别当前话题并保留相关消息
        var currentTopicMessages = IdentifyCurrentTopic(history, maxMessages / 2);
        result.AddRange(currentTopicMessages);
        
        // 3. 保留最近的消息（确保对话连续性）
        var recentMessages = history.TakeLast(maxMessages - result.Count).ToList();
        result.AddRange(recentMessages);
        
        // 4. 去重并确保不超过最大限制
        result = result.GroupBy(m => m.Content).Select(g => g.First()).Take(maxMessages).ToList();
        
        // 5. 按时间顺序排序
        result = result.OrderBy(m => m.Timestamp).ToList();
        
        return result;
    }
    
    /// <summary>
    /// 识别当前话题并提取相关消息
    /// </summary>
    private List<ChatMessage> IdentifyCurrentTopic(List<ChatMessage> history, int maxTopicMessages)
    {
        if (history.Count < 3)
            return new List<ChatMessage>();
        
        // 获取最近的消息作为话题分析基础
        var recentMessages = history.TakeLast(5).ToList();
        var lastUserMessage = recentMessages.LastOrDefault(m => m.Role == "user");
        
        if (lastUserMessage == null)
            return new List<ChatMessage>();
        
        // 提取关键词和话题
        var keywords = ExtractKeywords(lastUserMessage.Content);
        var topicMessages = new List<ChatMessage>();
        
        // 在历史中查找相关话题的消息
        foreach (var message in history)
        {
            if (IsMessageRelatedToTopic(message.Content, keywords))
            {
                topicMessages.Add(message);
                if (topicMessages.Count >= maxTopicMessages)
                    break;
            }
        }
        
        return topicMessages;
    }
    
    /// <summary>
    /// 提取消息中的关键词
    /// </summary>
    private List<string> ExtractKeywords(string message)
    {
        var keywords = new List<string>();
        
        // 简单的关键词提取逻辑（可扩展为更复杂的NLP处理）
        var words = message.Split(new[] { ' ', '，', '。', '？', '！', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        
        // 过滤掉常见虚词和短词
        var stopWords = new HashSet<string> { "的", "了", "在", "是", "我", "你", "他", "她", "它", "这", "那", "和", "与", "或", "但", "如果", "因为", "所以", "然后", "现在", "刚才", "请问", "谢谢", "你好" };
        
        foreach (var word in words)
        {
            if (word.Length >= 2 && !stopWords.Contains(word) && !keywords.Contains(word))
            {
                keywords.Add(word);
            }
        }
        
        return keywords.Take(5).ToList(); // 最多返回5个关键词
    }
    
    /// <summary>
    /// 判断消息是否与话题相关
    /// </summary>
    private bool IsMessageRelatedToTopic(string message, List<string> keywords)
    {
        if (string.IsNullOrEmpty(message) || keywords.Count == 0)
            return false;
        
        // 计算关键词匹配度
        var matchCount = keywords.Count(keyword => 
            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        return matchCount >= 1; // 至少匹配一个关键词
    }
    
    /// <summary>
    /// 判断是否为重要消息
    /// </summary>
    private bool IsImportantMessage(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        
        // 重要消息的特征
        var importantPatterns = new[]
        {
            "名字", "年龄", "地点", "时间", "日期", "地址", "电话", "邮箱",
            "密码", "账号", "身份", "职业", "爱好", "兴趣", "目标", "计划",
            "任务", "要求", "规则", "条件", "限制", "禁止", "必须", "需要"
        };
        
        return importantPatterns.Any(pattern => 
            content.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// 获取HTTP客户端用于API调用
    /// </summary>
    public HttpClient GetHttpClient()
    {
        // 确保HttpClient实例存在并正确配置
        if (_httpClient == null)
        {
            _httpClient = new HttpClient();
            ConfigureHttpClient();
        }
        
        return _httpClient;
    }
    
    /// <summary>
    /// 获取搜索专用HTTP客户端
    /// </summary>
    public HttpClient GetSearchHttpClient()
    {
        // 确保搜索HttpClient实例存在并正确配置
        if (_searchHttpClient == null)
        {
            _searchHttpClient = new HttpClient();
            ConfigureSearchHttpClient();
        }
        
        return _searchHttpClient;
    }
    
    /// <summary>
    /// 配置HTTP客户端参数
    /// </summary>
    private void ConfigureHttpClient()
    {
        if (_httpClient == null)
            return;
        
        // 设置请求超时
        _httpClient.Timeout = TimeSpan.FromSeconds(_requestTimeout);
        
        // 设置默认请求头
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        // 添加用户代理
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("AAEmu Game Client/1.0");
        
        // 设置连接池大小和保持活动时间
        var sp = _httpClient.DefaultRequestHeaders.ConnectionClose = false;
        
        Logger.Debug($"HTTP客户端已配置 - 超时: {_requestTimeout}秒");
    }
    
    /// <summary>
    /// 配置搜索专用HTTP客户端
    /// </summary>
    private void ConfigureSearchHttpClient()
    {
        if (_searchHttpClient == null)
            return;
        
        // 设置请求超时
        _searchHttpClient.Timeout = TimeSpan.FromSeconds(_searchTimeout);
        
        // 设置默认请求头
        _searchHttpClient.DefaultRequestHeaders.Accept.Clear();
        _searchHttpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        
        // 添加用户代理
        _searchHttpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("AAEmu Game Search Client/1.0");
        
        // 设置连接池大小和保持活动时间
        _searchHttpClient.DefaultRequestHeaders.ConnectionClose = false;
        
        // 如果有搜索API密钥，添加到请求头
        if (!string.IsNullOrEmpty(_searchApiKey))
        {
            _searchHttpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _searchApiKey);
        }
        
        Logger.Debug($"搜索HTTP客户端已配置 - 超时: {_searchTimeout}秒");
    }
    
    /// <summary>
    /// 游戏内容集成 - 查询游戏内信息
    /// </summary>
    public string QueryGameContent(string query)
    {
        if (!_gameContentIntegrationEnabled)
            return "游戏内容集成功能已禁用";
        
        try
        {
            var results = new List<string>();
            
            // 查询任务信息
            if (_enableQuestInfo)
            {
                var questResults = SearchQuests(query);
                if (!string.IsNullOrEmpty(questResults))
                    results.Add($"任务信息:\n{questResults}");
            }
            
            // 查询物品信息
            if (_enableItemInfo)
            {
                var itemResults = SearchItems(query);
                if (!string.IsNullOrEmpty(itemResults))
                    results.Add($"物品信息:\n{itemResults}");
            }
            
            // 查询NPC信息
            if (_enableNpcInfo)
            {
                var npcResults = SearchNpcs(query);
                if (!string.IsNullOrEmpty(npcResults))
                    results.Add($"NPC信息:\n{npcResults}");
            }
            
            return results.Count > 0 ? string.Join("\n\n", results) : "未找到相关的游戏内容信息";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "查询游戏内容时出错");
            return "查询游戏内容时发生错误";
        }
    }
    
    /// <summary>
    /// 搜索任务信息
    /// </summary>
    private string SearchQuests(string query)
    {
        try
        {
            var questManager = QuestManager.Instance;
            var results = new List<string>();
            
            // 由于QuestTemplate没有Name和Description属性，暂时禁用任务搜索
            // 或者使用其他方式获取任务信息
            return "任务信息搜索功能暂不可用";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "搜索任务信息时出错");
            return "搜索任务信息时发生错误";
        }
    }
    
    /// <summary>
    /// 搜索物品信息
    /// </summary>
    private string SearchItems(string query)
    {
        try
        {
            var itemManager = ItemManager.Instance;
            var results = new List<string>();
            
            // 获取所有物品模板
            var allItems = itemManager.GetAllItems();
            if (allItems == null)
                return "无法获取物品模板数据";
            
            var matchedItems = allItems
                .Where(i => i.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(_maxGameResults)
                .ToList();
            
            foreach (var item in matchedItems)
            {
                var itemInfo = $"物品ID: {item.Id}\n名称: {item.Name}\n类型: {item.GetType().Name}";
                results.Add(itemInfo);
            }
            
            return results.Count > 0 ? string.Join("\n\n", results) : "未找到匹配的物品";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "搜索物品信息时出错");
            return "搜索物品信息时发生错误";
        }
    }
    
    /// <summary>
    /// 搜索NPC信息
    /// </summary>
    private string SearchNpcs(string query)
    {
        try
        {
            var npcManager = NpcManager.Instance;
            var results = new List<string>();
            
            // 获取所有NPC模板
            var allNpcs = npcManager.GetAllTemplates().Values;
            if (allNpcs == null)
                return "无法获取NPC模板数据";
            
            var matchedNpcs = allNpcs
                .Where(n => n.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
                .Take(_maxGameResults)
                .ToList();
            
            foreach (var npc in matchedNpcs)
            {
                var npcInfo = $"NPC ID: {npc.Id}\n名称: {npc.Name}\n等级: {npc.Level}\n阵营: {npc.FactionId}";
                results.Add(npcInfo);
            }
            
            return results.Count > 0 ? string.Join("\n\n", results) : "未找到匹配的NPC";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "搜索NPC信息时出错");
            return "搜索NPC信息时发生错误";
        }
    }
    
    /// <summary>
    /// 获取游戏内容集成状态
    /// </summary>
    public Dictionary<string, object> GetGameContentStatus()
    {
        return new Dictionary<string, object>
        {
            ["enabled"] = _gameContentIntegrationEnabled,
            ["maxResults"] = _maxGameResults,
            ["questInfoEnabled"] = _enableQuestInfo,
            ["itemInfoEnabled"] = _enableItemInfo,
            ["npcInfoEnabled"] = _enableNpcInfo
        };
    }
}

/// <summary>
/// AI配置信息
/// </summary>
public class AIConfig
{
    public bool IsEnabled { get; set; }
    public string ApiUrl { get; set; }
    public string Model { get; set; }
    public string ApiKey { get; set; }
    public int MaxTokens { get; set; } = 500;
    public double Temperature { get; set; } = 0.7;
    public int ContextWindow { get; set; } = 10;
    public bool MemoryEnabled { get; set; } = true;
    public bool EmotionEnabled { get; set; } = false;
    public bool CacheEnabled { get; set; } = false;
    public int ResponseDelay { get; set; } = 100;
    public int SessionTimeout { get; set; } = 30;
    
    // 搜索相关配置
    public bool SearchEnabled { get; set; } = true;
    public string SearchApiUrl { get; set; } = "";
    public string SearchApiKey { get; set; } = "";
    public int MaxSearchResults { get; set; } = 3;
    public int SearchTimeout { get; set; } = 15;
}

/// <summary>
/// AI聊天会话信息
/// </summary>
public class AIChatSession
{
    public uint CharacterId { get; set; }
    public uint NpcId { get; set; }
    public string NpcName { get; set; }
    public List<ChatMessage> ChatHistory { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public int ContextWindow { get; set; } = 10;
    
    /// <summary>
    /// 获取会话持续时间
    /// </summary>
    public TimeSpan GetDuration()
    {
        return DateTime.UtcNow - StartTime;
    }
    
    /// <summary>
    /// 获取聊天历史中的消息数量
    /// </summary>
    public int GetMessageCount()
    {
        return ChatHistory?.Count ?? 0;
    }
    
    /// <summary>
    /// 获取压缩后的上下文（使用智能压缩算法）
    /// </summary>
    public string GetCompressedContext()
    {
        if (ChatHistory == null || ChatHistory.Count == 0)
            return string.Empty;
            
        // 使用智能上下文压缩算法
        var compressed = SmartContextCompression(ChatHistory, ContextWindow);
        return string.Join("\n", compressed.Select(m => $"{m.Role}: {m.Content}"));
    }
    
    /// <summary>
    /// 智能上下文压缩算法（会话级别实现）
    /// </summary>
    private List<ChatMessage> SmartContextCompression(List<ChatMessage> history, int maxMessages)
    {
        if (history.Count <= maxMessages)
            return history.ToList();
        
        var result = new List<ChatMessage>();
        
        // 1. 保留系统消息和重要信息
        var importantMessages = history.Where(m => 
            m.Role == "system" || 
            m.Content.Contains("重要") ||
            m.Content.Contains("关键") ||
            m.Content.Contains("记住") ||
            m.Content.Length > 100 ||
            IsImportantMessage(m.Content)
        ).Take(2).ToList();
        
        result.AddRange(importantMessages);
        
        // 2. 识别当前话题并保留相关消息
        var currentTopicMessages = IdentifyCurrentTopic(history, maxMessages / 2);
        result.AddRange(currentTopicMessages);
        
        // 3. 保留最近的消息（确保对话连续性）
        var recentMessages = history.TakeLast(maxMessages - result.Count).ToList();
        result.AddRange(recentMessages);
        
        // 4. 去重并确保不超过最大限制
        result = result.GroupBy(m => m.Content).Select(g => g.First()).Take(maxMessages).ToList();
        
        // 5. 按时间顺序排序
        result = result.OrderBy(m => m.Timestamp).ToList();
        
        return result;
    }
    
    /// <summary>
    /// 识别当前话题并提取相关消息
    /// </summary>
    private List<ChatMessage> IdentifyCurrentTopic(List<ChatMessage> history, int maxTopicMessages)
    {
        if (history.Count < 3)
            return new List<ChatMessage>();
        
        // 获取最近的消息作为话题分析基础
        var recentMessages = history.TakeLast(5).ToList();
        var lastUserMessage = recentMessages.LastOrDefault(m => m.Role == "user");
        
        if (lastUserMessage == null)
            return new List<ChatMessage>();
        
        // 提取关键词和话题
        var keywords = ExtractKeywords(lastUserMessage.Content);
        var topicMessages = new List<ChatMessage>();
        
        // 在历史中查找相关话题的消息
        foreach (var message in history)
        {
            if (IsMessageRelatedToTopic(message.Content, keywords))
            {
                topicMessages.Add(message);
                if (topicMessages.Count >= maxTopicMessages)
                    break;
            }
        }
        
        return topicMessages;
    }
    
    /// <summary>
    /// 提取消息中的关键词
    /// </summary>
    private List<string> ExtractKeywords(string message)
    {
        var keywords = new List<string>();
        
        // 简单的关键词提取逻辑（可扩展为更复杂的NLP处理）
        var words = message.Split(new[] { ' ', '，', '。', '？', '！', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries);
        
        // 过滤掉常见虚词和短词
        var stopWords = new HashSet<string> { "的", "了", "在", "是", "我", "你", "他", "她", "它", "这", "那", "和", "与", "或", "但", "如果", "因为", "所以", "然后", "现在", "刚才", "请问", "谢谢", "你好" };
        
        foreach (var word in words)
        {
            if (word.Length >= 2 && !stopWords.Contains(word) && !keywords.Contains(word))
            {
                keywords.Add(word);
            }
        }
        
        return keywords.Take(5).ToList(); // 最多返回5个关键词
    }
    
    /// <summary>
    /// 判断消息是否与话题相关
    /// </summary>
    private bool IsMessageRelatedToTopic(string message, List<string> keywords)
    {
        if (string.IsNullOrEmpty(message) || keywords.Count == 0)
            return false;
        
        // 计算关键词匹配度
        var matchCount = keywords.Count(keyword => 
            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        
        return matchCount >= 1; // 至少匹配一个关键词
    }
    
    /// <summary>
    /// 判断是否为重要消息
    /// </summary>
    private bool IsImportantMessage(string content)
    {
        if (string.IsNullOrEmpty(content))
            return false;
        
        // 重要消息的特征
        var importantPatterns = new[]
        {
            "名字", "年龄", "地点", "时间", "日期", "地址", "电话", "邮箱",
            "密码", "账号", "身份", "职业", "爱好", "兴趣", "目标", "计划",
            "任务", "要求", "规则", "条件", "限制", "禁止", "必须", "需要"
        };
        
        return importantPatterns.Any(pattern => 
            content.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// 添加消息到聊天历史
    /// </summary>
    public void AddMessage(string role, string content)
    {
        ChatHistory ??= new List<ChatMessage>();
        ChatHistory.Add(new ChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.UtcNow
        });
        
        // 限制历史记录大小
        if (ChatHistory.Count > ContextWindow * 2)
        {
            ChatHistory = ChatHistory.TakeLast(ContextWindow).ToList();
        }
    }
}

/// <summary>
/// 聊天消息
/// </summary>
public class ChatMessage
{
    public string Role { get; set; } // "user", "assistant", "system"
    public string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}