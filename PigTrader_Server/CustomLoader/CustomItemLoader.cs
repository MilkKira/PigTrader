using System.Reflection;

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WTTServerCommonLib.Services;

namespace PigTrader_Server.CustomLoader;

/// <summary>
/// 自定义物品加载器，负责将自定义武器、弹药、配件、护甲及藏身处配方注册到服务器数据库。
/// 通过 WTTServerCommonLib 的 API 批量加载并写入，再由 WeaponSlotComTool 应用武器插槽兼容性补丁。
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class CustomItemLoader(ISptLogger<CustomItemLoader> customlogger, global::WTTServerCommonLib.WTTServerCommonLib wttCommon, DatabaseService databaseService)
{
    /// <summary>
    /// 异步加载所有自定义物品与藏身处配方。
    /// 外部库的同步 I/O（CreateCustomItems / CreateHideoutRecipes）通过 Task.Run 移到线程池，
    /// 内部文件读取（WeaponSlotComTool）使用真正的异步 I/O。
    /// </summary>
    public async Task LoadCustom()
    {
        // 第 1 步：加载外部库的自定义物品（同步 API，通过 Task.Run 不阻塞启动线程）
        await Task.Run(() =>
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string modRoot = Path.GetDirectoryName(assembly.Location) ?? "";
            string[] array = new string[] { "data/CustomItem/Weapons", "data/CustomItemAmmo", "data/CustomItemAttachments", "data/CustomItemItems", "data/CustomItemArmor" };
            foreach (string text in array)
            {
                if (Directory.Exists(Path.Combine(modRoot, text)))
                {
                    WTTCustomItemServiceExtended customItemServiceExtended = wttCommon.CustomItemServiceExtended;
                    customItemServiceExtended.CreateCustomItems(assembly, Path.Join(text));
                }
            }

            WTTCustomHideoutRecipeService customHideoutRecipeService = wttCommon.CustomHideoutRecipeService;
            string customRecipes = "data/CustomRecipes/Recipes";
            customHideoutRecipeService.CreateHideoutRecipes(assembly, Path.Join(customRecipes));
        }).ConfigureAwait(false);

        // 第 2 步：应用武器插槽兼容性补丁（使用异步文件 I/O 读取 JSON）
        try
        {
            await WeaponSlotComTool.ApplyAsync(databaseService, Assembly.GetExecutingAssembly()).ConfigureAwait(false);
        }
        catch
        {
            // 兼容性补丁非必需，静默跳过
        }

        customlogger.LogWithColor("[PigTrader] Loaded Custom Items and Recipes]", LogTextColor.Green, LogBackgroundColor.Black);
    }
}
