using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;

namespace PigTrader_Server.Tools;

/// <summary>
/// 流畅API（Fluent API）风格的商人库存（Assort）创建器。
/// 支持链式调用，依次配置物品模板、堆叠数量、价格、忠诚度等级等，
/// 最终调用 Export() 将组装好的库存写入指定商人的数据库中。
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class FluentTraderAssortCreator(
    DatabaseService databaseService,
    ISptLogger<FluentTraderAssortCreator> logger)
{
    private readonly List<Item> _itemsToSell = [];
    private readonly Dictionary<string, List<List<BarterScheme>>> _barterScheme = new();
    private readonly Dictionary<string, int> _loyaltyLevel = new();

    /// <summary>
    /// 创建一个简单的单一物品条目，准备插入商人库存表
    /// </summary>
    /// <param name="itemTpl">物品模板ID（可在 db.sp-tarkov.com 查询）</param>
    /// <param name="itemId">可选的物品实例ID，不传则自动生成</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator CreateSingleAssortItem(MongoId itemTpl, MongoId? itemId = null)
    {
        var newItemToAdd = new Item
        {
            Id = itemId ?? new MongoId(),
            Template = itemTpl,
            ParentId = "hideout", // 固定为 "hideout"
            SlotId = "hideout",   // 固定为 "hideout"
            Upd = new Upd
            {
                UnlimitedCount = false,
                StackObjectsCount = 100
            }
        };

        _itemsToSell.Add(newItemToAdd);
        return this;
    }

    /// <summary>
    /// 创建复杂物品（如已组装好的武器）。
    /// items[0] 为根物品，其余为附加配件。
    /// </summary>
    /// <param name="items">包含根物品和所有配件的物品列表</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator CreateComplexAssortItem(List<Item> items)
    {
        items[0].ParentId = "hideout";
        items[0].SlotId = "hideout";
        items[0].Upd ??= new Upd();
        items[0].Upd.UnlimitedCount = false;
        items[0].Upd.StackObjectsCount = 100;

        _itemsToSell.AddRange(items);
        return this;
    }

    /// <summary>
    /// 设置最新添加物品的堆叠数量
    /// </summary>
    /// <param name="stackCount">堆叠数量</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddStackCount(int stackCount)
    {
        _itemsToSell[0].Upd.StackObjectsCount = stackCount;
        return this;
    }

    /// <summary>
    /// 设置最新添加物品为无限库存（999999 堆叠 + 无限计数标志）
    /// </summary>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddUnlimitedStackCount()
    {
        _itemsToSell[0].Upd.StackObjectsCount = 999999;
        _itemsToSell[0].Upd.UnlimitedCount = true;
        return this;
    }

    /// <summary>
    /// 单独设置最新添加物品的堆叠数量为 999999（不开启无限计数标志）
    /// </summary>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator MakeStackCountUnlimited()
    {
        _itemsToSell[0].Upd.StackObjectsCount = 999999;
        return this;
    }

    /// <summary>
    /// 设置每次商人刷新期间的购买数量上限
    /// </summary>
    /// <param name="maxBuyLimit">最大购买次数</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddBuyRestriction(int maxBuyLimit)
    {
        _itemsToSell[0].Upd.BuyRestrictionMax = maxBuyLimit;
        _itemsToSell[0].Upd.BuyRestrictionCurrent = 0;
        return this;
    }

    /// <summary>
    /// 设置购买物品所需的最低商人忠诚度等级
    /// </summary>
    /// <param name="level">忠诚度等级（1-4）</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddLoyaltyLevel(int level)
    {
        _loyaltyLevel[_itemsToSell[0].Id] = level;
        return this;
    }

    /// <summary>
    /// 设置物品的货币价格（卢布/美元/欧元）
    /// </summary>
    /// <param name="currencyType">货币类型模板ID（参见 Money 常量类）</param>
    /// <param name="amount">价格数量</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddMoneyCost(string currencyType, int amount)
    {
        var dataToAdd = new BarterScheme
        {
            Count = amount,
            Template = currencyType
        };

        if (!_barterScheme.TryAdd(_itemsToSell[0].Id, [[dataToAdd]]))
        {
            logger.Warning($"无法添加货币价格: {currencyType}");
        }

        return this;
    }

    /// <summary>
    /// 添加以物易物的交换成本。
    /// 如果该物品已有同类的交换条件，则累加所需数量。
    /// </summary>
    /// <param name="itemTpl">所需交换物品的模板ID</param>
    /// <param name="count">所需数量</param>
    /// <returns>当前实例（支持链式调用）</returns>
    public FluentTraderAssortCreator AddBarterCost(MongoId itemTpl, int count)
    {
        var sellableItemId = _itemsToSell[0].Id;

        if (_barterScheme.Count == 0)
        {
            var dataToAdd = new BarterScheme
            {
                Count = count,
                Template = itemTpl
            };
            _barterScheme[sellableItemId] = [[dataToAdd]];
        }
        else
        {
            var existingData = _barterScheme[sellableItemId][0].FirstOrDefault(x => x.Template == itemTpl);
            if (existingData is not null)
            {
                existingData.Count += count;
            }
            else
            {
                _barterScheme[sellableItemId][0].Add(new BarterScheme
                {
                    Count = count,
                    Template = itemTpl
                });
            }
        }

        return this;
    }

    /// <summary>
    /// 将临时生成的库存数据写入服务器数据库中指定的商人。
    /// 写入后清空临时数据，以便创建下一批库存。
    /// </summary>
    /// <param name="traderId">目标商人ID</param>
    /// <returns>成功返回当前实例，失败（物品ID冲突）返回 null</returns>
    public FluentTraderAssortCreator? Export(string traderId)
    {
        var traderData = databaseService.GetTables().Traders.GetValueOrDefault(traderId);

        var rootItemAddedId = _itemsToSell.FirstOrDefault().Id;
        if (traderData.Assort.Items.Exists(x => x.Id == rootItemAddedId))
        {
            logger.Error($"无法添加物品（ID 已存在）: {_itemsToSell[0].Id}");

            _itemsToSell.Clear();
            _barterScheme.Clear();
            _loyaltyLevel.Clear();

            return null;
        }

        traderData.Assort.Items.AddRange(_itemsToSell);
        traderData.Assort.BarterScheme[rootItemAddedId] = _barterScheme[rootItemAddedId];
        traderData.Assort.LoyalLevelItems[rootItemAddedId] = _loyaltyLevel[rootItemAddedId];

        // 清空临时数据
        _itemsToSell.Clear();
        _barterScheme.Clear();
        _loyaltyLevel.Clear();

        return this;
    }
}
