using System.Reflection;

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using WTTServerCommonLib.Services;

namespace PigTrader_Server.CustomLoader;
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class CustomItemLoader(ISptLogger<CustomItemLoader> customlogger, global::WTTServerCommonLib.WTTServerCommonLib wttCommon, DatabaseService databaseService)
{
    public Task LoadCustom()    
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string modRoot = Path.GetDirectoryName(assembly.Location) ?? "";
        string[] array = new string[] { "Weapons", "Ammo", "Attachments", "Items", "Armor" };
        foreach (string text in array)
        {
            if (Directory.Exists(Path.Combine(modRoot, text)))
            {
                WTTCustomItemServiceExtended customItemServiceExtended = wttCommon.CustomItemServiceExtended;
                Assembly assembly2 = assembly;
                string text2 = text;
                customItemServiceExtended.CreateCustomItems(assembly2, Path.Join(new ReadOnlySpan<string>(ref text2)));
            }
        }
        
        string[] array2 = null;
        WTTCustomHideoutRecipeService customHideoutRecipeService = wttCommon.CustomHideoutRecipeService;
        Assembly assembly3 = assembly;
        string text3 = "Recipes";
        customHideoutRecipeService.CreateHideoutRecipes(assembly3, Path.Join(new ReadOnlySpan<string>(ref text3)));
        try
        {
             WeaponSlotComTool.Apply(databaseService, assembly);
        }
        catch
        {
            
        }
        customlogger.Info("[SALCO'S ARSENAL v1.0.4 successfully loaded]");
        
        return Task.CompletedTask;
    }
}