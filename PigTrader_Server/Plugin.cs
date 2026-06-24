using System.Reflection;
using PigTrader_Server.Helper;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
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
    AddCustomTraderHelper addCustomTraderHelper
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

        logger.LogWithColor("PigTrader Loaded", LogTextColor.Green , LogBackgroundColor.Black);
        
        return Task.CompletedTask;
    }
}
