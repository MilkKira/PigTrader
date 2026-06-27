using System.Reflection;

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WTTServerCommonLib.Services;

namespace PigTrader_Server.CustomLoader;
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class CustomItemLoader(ISptLogger<CustomItemLoader> customlogger, global::WTTServerCommonLib.WTTServerCommonLib wttCommon, DatabaseService databaseService)
{
    public async Task LoadCustom()
    {
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
        try
        {
            await WeaponSlotComTool.ApplyAsync(databaseService, Assembly.GetExecutingAssembly()).ConfigureAwait(false);
        }
        catch
        {
            
        }
        customlogger.LogWithColor("[PigTrader] Loaded Custom Items and Recipes]", LogTextColor.Green, LogBackgroundColor.Black);
    }
}
