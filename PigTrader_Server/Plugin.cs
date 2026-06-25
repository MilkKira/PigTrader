using System.Reflection;
using PigTrader_Server.Helper;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Services.Mod;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Path = System.IO.Path;

namespace PigTrader_Server;

/// <summary>
/// PigTrader 模组主入口。
/// 在数据库加载完成后（PostDBModLoader）执行，依次完成：
/// 1) 加载商人基础数据并注册到数据库
/// 2) 注册商人头像路由
/// 3) 将商人加入跳蚤市场可见列表
/// 4) 添加本地化文字
/// 5) 从 quests.json 加载任务（任务）到服务器
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class PigTraderPlugin(
    ISptLogger<PigTraderPlugin> logger,
    ModHelper modHelper,
    DatabaseService databaseService,
    ImageRouter imageRouter,
    ConfigServer configServer,
    TimeUtil timeUtil,
    ICloner cloner,
    AddCustomTraderHelper addCustomTraderHelper,
    CustomQuestService customQuestService
    ) : IOnLoad
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    /// <summary>
    /// 模组主加载方法，服务器启动时自动调用。
    /// 依次执行：读取配置 → 注册头像 → 注册商人 → 添加本地化 → 加载任务
    /// </summary>
    /// <returns>异步任务结果</returns>
    public Task OnLoad()
    {
        // Step 1: 获取模组所在文件夹的绝对路径
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        // Step 2: 加载商人头像图片路径
        var traderImagePath = Path.Combine(pathToMod, "data/pig.jpg");
        
        logger.LogWithColor("1", LogTextColor.Green, LogBackgroundColor.Black);

        // Step 3: 从 data/base.json 读取商人基础配置
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "data/base.json");
        
        logger.LogWithColor("2", LogTextColor.Green, LogBackgroundColor.Black);

        // Step 4: 注册头像路由，使客户端可以加载商人头像
        imageRouter.AddRoute(traderBase.Avatar.Replace(".jpg", ""), traderImagePath);
        
        logger.LogWithColor("3", LogTextColor.Green, LogBackgroundColor.Black);

        // Step 5: 将商人加入跳蚤市场（Ragfair）可见列表
        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        
        logger.LogWithColor("4", LogTextColor.Green, LogBackgroundColor.Black);

        // Step 6: 将商人（空库存）写入数据库
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);

        // Step 7: 为商人添加多国语言文本（各语言环境下均可见）
        addCustomTraderHelper.AddTraderToLocales(traderBase, "Pig", "This is a cute pig shop.");

        // Step 8: 从 data/quests.json 加载任务到服务器数据库
        LoadQuestsFromJson(pathToMod, traderBase.Id);

        // Step 9: 输出成功日志
        logger.LogWithColor("PigTrader Loaded", LogTextColor.Green, LogBackgroundColor.Black);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 data/quests.json 文件中读取任务数据，
    /// 使用 CustomQuestService 逐一注册到服务器数据库，
    /// 并将任务关联到当前商人。
    /// </summary>
    /// <param name="pathToMod">模组根目录路径</param>
    /// <param name="traderId">当前商人的ID（用于关联任务）</param>
    private void LoadQuestsFromJson(string pathToMod, string traderId)
    {
        var questsFilePath = Path.Combine(pathToMod, "data/quests.json");

        // 检查文件是否存在
        if (!File.Exists(questsFilePath))
        {
            logger.Warning($"任务文件不存在: {questsFilePath}");
            return;
        }

        // 反序列化 JSON 为 Quest 对象列表
        var quests = modHelper.GetJsonDataFromFile<List<Quest>>(pathToMod, "data/quests.json");
        if (quests == null || quests.Count == 0)
        {
            logger.Warning("quests.json 中未找到任务数据");
            return;
        }

        logger.Success($"正在加载 {quests.Count} 个任务");

        // 遍历每个任务，构造 NewQuestDetails 并注册到数据库
        foreach (var quest in quests)
        {
            // 构建中英文本地化文字字典
            var locales = new Dictionary<string, Dictionary<string, string>>
            {
                {
                    "en", new Dictionary<string, string>
                    {
                        // 本地化键值格式: "{questId} name" / "{questId} description"
                        { $"{quest.Id} name", quest.QuestName },
                        { $"{quest.Id} description", $"Quest: {quest.QuestName}" }
                    }
                },
                {
                    "zh", new Dictionary<string, string>
                    {
                        { $"{quest.Id} name", quest.QuestName },
                        { $"{quest.Id} description", $"任务: {quest.QuestName}" }
                    }
                }
            };

            // 调用 CustomQuestService 创建任务
            var result = customQuestService.CreateQuest(new NewQuestDetails
            {
                NewQuest = quest,
                Locales = locales,
                LockedToSide = null // null 表示 USEC 和 BEAR 均可接取
            });

            if (result != null)
            {
                logger.Success($"任务创建成功: {quest.QuestName}");
            }
            else
            {
                logger.Error($"任务创建失败: {quest.QuestName}");
            }
        }
    }
}
