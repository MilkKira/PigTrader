using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;

namespace PigTrader_Server.Helper
{
    /// <summary>
    /// 商人注册辅助类。
    /// 提供将新商人写入服务器数据库、添加刷新时间、添加本地化文字等功能。
    /// </summary>
    [Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
    public class AddCustomTraderHelper(
        ICloner cloner,
        DatabaseService databaseService)
    {
        /// <summary>
        /// 设置商人的库存刷新时间。
        /// 商人的商品会在 min~max 秒的时间范围内随机刷新。
        /// </summary>
        /// <param name="traderConfig">商人配置对象（从 ConfigServer 获取）</param>
        /// <param name="baseJson">商人的基础 JSON 数据（含 ID）</param>
        /// <param name="refreshTimeSecondsMin">最小刷新间隔（秒）</param>
        /// <param name="refreshTimeSecondsMax">最大刷新间隔（秒）</param>
        public void SetTraderUpdateTime(
            TraderConfig traderConfig,
            TraderBase baseJson,
            int refreshTimeSecondsMin,
            int refreshTimeSecondsMax)
        {
            var traderRefreshRecord = new UpdateTime
            {
                TraderId = baseJson.Id,
                Seconds = new MinMax<int>(refreshTimeSecondsMin, refreshTimeSecondsMax)
            };

            traderConfig.UpdateTime.Add(traderRefreshRecord);
        }

        /// <summary>
        /// 将商人以空库存的形式注册到服务器数据库。
        /// 刚注册的商人没有商品可卖，需要通过 FluentTraderAssortCreator
        /// 或 assord.json 补充库存。
        /// </summary>
        /// <param name="traderDetailsToAdd">商人的基础配置数据</param>
        public void AddTraderWithEmptyAssortToDb(TraderBase traderDetailsToAdd)
        {
            // 创建空库存对象
            var emptyTraderItemAssortObject = new TraderAssort
            {
                Items = [],
                BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
                LoyalLevelItems = new Dictionary<MongoId, int>()
            };

            // 组装商人完整数据
            var traderDataToAdd = new Trader
            {
                Assort = emptyTraderItemAssortObject,
                Base = cloner.Clone(traderDetailsToAdd),
                QuestAssort = new()
                {
                    { "Started", new() },
                    { "Success", new() },
                    { "Fail", new() }
                },
                Dialogue = []
            };

            // 写入数据库
            if (!databaseService.GetTables().Traders.TryAdd(traderDetailsToAdd.Id, traderDataToAdd))
            {
                // 添加失败（ID 冲突等情况），静默处理
            }
        }

        /// <summary>
        /// 为商人添加各语言的本地化文字（名字、昵称、描述等）。
        /// 使用 AddTransformer 延迟加载机制，确保各语言请求时都能正确包含。
        /// </summary>
        /// <param name="baseJson">商人的基础配置（含名字、昵称、位置等）</param>
        /// <param name="firstName">商人名（如 "Cat"、"Pig"）</param>
        /// <param name="description">商人描述文字</param>
        public void AddTraderToLocales(TraderBase baseJson, string firstName, string description)
        {
            var locales = databaseService.GetTables().Locales.Global;
            var newTraderId = baseJson.Id;
            var fullName = baseJson.Name;
            var nickName = baseJson.Nickname;
            var location = baseJson.Location;

            foreach (var (localeKey, localeKvP) in locales)
            {
                // 使用 Transformer 延迟加载，避免内存占用过大
                localeKvP.AddTransformer(lazyloadedLocaleData =>
                {
                    lazyloadedLocaleData.Add($"{newTraderId} FullName", fullName);
                    lazyloadedLocaleData.Add($"{newTraderId} FirstName", firstName);
                    lazyloadedLocaleData.Add($"{newTraderId} Nickname", nickName);
                    lazyloadedLocaleData.Add($"{newTraderId} Location", location);
                    lazyloadedLocaleData.Add($"{newTraderId} Description", description);
                    return lazyloadedLocaleData;
                });
            }
        }

        /// <summary>
        /// 直接覆盖指定商人的所有库存数据（从 assord.json 读取后调用）。
        /// </summary>
        /// <param name="traderId">目标商人ID</param>
        /// <param name="newAssorts">新的库存数据</param>
        public void OverwriteTraderAssort(string traderId, TraderAssort newAssorts)
        {
            if (!databaseService.GetTables().Traders.TryGetValue(traderId, out var traderToEdit))
            {
                return;
            }

            traderToEdit.Assort = newAssorts;
        }
    }
}
