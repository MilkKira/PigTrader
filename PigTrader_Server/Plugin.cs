using System.Reflection;
using PigTrader_Server.Helper;
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
#pragma warning disable CS0618 // 类型或成员已过时

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
    
    public Task OnLoad()
    {
        // 获取模组路径
        var pathToMod = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        
        // 商人图标
        var traderImagePath = Path.Combine(pathToMod, "db/pig.jpg");
        
        // 商人Base数据
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(pathToMod, "data/base.json");
        
        imageRouter.AddRoute(traderBase.Avatar.Replace(".jpg", ""), traderImagePath);
        
        // addCustomTraderHelper.SetTraderUpdateTime(_traderConfig, traderBase, timeUtil.GetHoursAsSeconds(1), timeUtil.GetHoursAsSeconds(2));
        
        _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
        
        addCustomTraderHelper.AddTraderWithEmptyAssortToDb(traderBase);

        // Add localisation text for our trader to the database so it shows to people playing in different languages
        addCustomTraderHelper.AddTraderToLocales(traderBase, "Pig", "This is a cute pig shop.");

        // 从JSON文件加载任务
        LoadQuestsFromJson(pathToMod, traderBase.Id);

        logger.LogWithColor("PigTrader Loaded", LogTextColor.Green , LogBackgroundColor.Black);
        
        return Task.CompletedTask;
    }
    
    private void LoadQuestsFromJson(string pathToMod, string traderId)
    {
        var questsFilePath = Path.Combine(pathToMod, "data/quests.json");
        if (!File.Exists(questsFilePath))
        {
            logger.Warning($"Quest file not found: {questsFilePath}");
            return;
        }

        var quests = modHelper.GetJsonDataFromFile<List<Quest>>(pathToMod, "data/quests.json");
        if (quests == null || quests.Count == 0)
        {
            logger.Warning("No quests found in quests.json");
            return;
        }

        logger.Success($"Loading {quests.Count} quest(s) from quests.json");

        foreach (var quest in quests)
        {
            var locales = new Dictionary<string, Dictionary<string, string>>
            {
                {
                    "en", new Dictionary<string, string>
                    {
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

            var result = customQuestService.CreateQuest(new NewQuestDetails
            {
                NewQuest = quest,
                Locales = locales,
                LockedToSide = null
            });

            if (result != null)
            {
                logger.Success($"Quest created successfully: {quest.QuestName}");
            }
            else
            {
                logger.Error($"Failed to create quest: {quest.QuestName}");
            }
        }
    }
}
