using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SPTarkov.Server.Core.Services;

namespace PigTrader_Server.CustomLoader;

/// <summary>
/// 武器插槽兼容性工具。
/// 读取 "Weapons/*.json" 中的 SalcosCompat 配置，通过反射修改内存中武器模板的插槽过滤器，
/// 允许自定义物品兼容到特定武器插槽（如导轨、瞄具座等）。
/// </summary>
internal static class WeaponSlotComTool
{
    /// <summary>
    /// 异步应用武器插槽兼容性补丁。
    /// 读取武器目录下的 JSON 配置文件，解析后通过反射写入内存数据库。
    /// </summary>
    /// <param name="databaseService">服务器数据库服务</param>
    /// <param name="assembly">当前程序集（用于定位 Weapons/ 目录）</param>
    public static async Task ApplyAsync(DatabaseService databaseService, Assembly assembly)
    {
        var modRoot = Path.GetDirectoryName(assembly.Location) ?? "";
        if (string.IsNullOrWhiteSpace(modRoot))
            return;

        var weaponsDir = Path.Combine(modRoot, "Weapons");
        if (!Directory.Exists(weaponsDir))
            return;

        var compatDict = await LoadCompatFromWeaponsAsync(weaponsDir).ConfigureAwait(false);
        if (compatDict.Count == 0)
            return;

        ApplyWeaponCompat(databaseService.GetTables().Templates.Items, compatDict);
    }

    /// <summary>
    /// 从 Weapons/ 目录异步读取所有 JSON 兼容配置文件。
    /// </summary>
    /// <param name="weaponsDir">武器目录路径</param>
    /// <returns>武器 ID 到兼容配置的映射表</returns>
    private static async Task<Dictionary<string, WeaponSlotCompatConfig>> LoadCompatFromWeaponsAsync(string weaponsDir)
    {
        var result = new Dictionary<string, WeaponSlotCompatConfig>(StringComparer.Ordinal);

        var files = Directory.GetFiles(weaponsDir, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    continue;

                var wrapper = JsonSerializer.Deserialize<WeaponSlotCompatWrapper>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                var config = wrapper?.SalcosCompat;
                if (config != null && !string.IsNullOrWhiteSpace(config.Id))
                {
                    result[config.Id] = config;
                }
            }
            catch
            {
                // 跳过格式异常的文件
            }
        }

        return result;
    }

    /// <summary>
    /// 将加载的兼容配置写入内存数据库的武器模板对象。
    /// </summary>
    /// <param name="itemsDict">内存中所有物品模板的字典（ID -> 物品对象）</param>
    /// <param name="compatById">武器 ID 到兼容配置的映射表</param>
    private static void ApplyWeaponCompat(IDictionary itemsDict, Dictionary<string, WeaponSlotCompatConfig> compatById)
    {
        if (itemsDict.Count == 0 || compatById.Count == 0)
            return;

        foreach (var kvp in compatById)
        {
            if (!itemsDict.Contains(kvp.Key))
                continue;

            var item = itemsDict[kvp.Key];
            if (item == null)
                continue;

            var overrides = kvp.Value.SlotOverrides;
            if (overrides is { Count: > 0 })
            {
                ApplySlotOverrides(item, overrides);
            }
        }
    }

    private static void ApplySlotOverrides(object templateItem, List<WeaponSlotOverride> overrides)
    {
        var props = GetMemberValue(templateItem, "_props") ?? GetMemberValue(templateItem, "Properties");
        if (props == null)
            return;

        var allSlots = new List<object>();

        var slots = GetMemberValue(props, "Slots") ?? GetMemberValue(props, "slots");
        if (slots is IEnumerable enumerable and not string)
        {
            foreach (var slot in enumerable)
            {
                if (slot != null)
                    allSlots.Add(slot);
            }
        }

        var chambers = GetMemberValue(props, "Chambers") ?? GetMemberValue(props, "chambers");
        if (chambers is IEnumerable chamberEnumerable and not string)
        {
            foreach (var chamber in chamberEnumerable)
            {
                if (chamber != null)
                    allSlots.Add(chamber);
            }
        }

        if (allSlots.Count == 0)
            return;

        foreach (var ov in overrides)
        {
            if (string.IsNullOrWhiteSpace(ov.SlotName))
                continue;

            var slot = FindSlotByName(allSlots, ov.SlotName);
            if (slot != null)
            {
                ApplySlotOverride(slot, ov);
            }
        }
    }

    private static object? FindSlotByName(List<object> slots, string slotName)
    {
        foreach (var slot in slots)
        {
            var nameObj = GetMemberValue(slot, "_name") ?? GetMemberValue(slot, "Name");
            var name = nameObj?.ToString();
            if (name != null && string.Equals(name, slotName, StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }
        }

        return null;
    }

    private static void ApplySlotOverride(object slot, WeaponSlotOverride ov)
    {
        var props = GetMemberValue(slot, "_props") ?? GetMemberValue(slot, "Properties");
        if (props == null)
            return;

        var filters = (GetMemberValue(props, "filters") ?? GetMemberValue(props, "Filters")) as IList;
        if (filters == null || filters.Count == 0)
            return;

        var filterEntry = filters[0];
        if (filterEntry == null)
            return;

        var currentFilter = GetMemberValue(filterEntry, "Filter") ?? GetMemberValue(filterEntry, "filter");
        var filterList = EnsureStringList(filterEntry, "Filter", currentFilter);
        if (filterList == null)
            return;

        if (ov.FilterTpls is { Count: > 0 })
        {
            foreach (var tpl in ov.FilterTpls)
            {
                if (!string.IsNullOrWhiteSpace(tpl) && !ContainsString(filterList, tpl))
                {
                    filterList.Add(tpl);
                }
            }
        }

        if (ov.ClearExcludedFilter)
        {
            var currentExcluded = GetMemberValue(filterEntry, "ExcludedFilter") ?? GetMemberValue(filterEntry, "excludedFilter");
            var excludedList = EnsureStringList(filterEntry, "ExcludedFilter", currentExcluded);
            if (excludedList == null)
                return;

            excludedList.Clear();
        }
    }

    private static bool ContainsString(IList list, string value)
    {
        foreach (var item in list)
        {
            if (item != null && string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static object? GetMemberValue(object obj, string name)
    {
        if (obj == null)
            return null;

        try
        {
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop != null)
                return prop.GetValue(obj);

            var field = type.GetField(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(obj);
        }
        catch
        {
            // 忽略反射错误
        }

        return null;
    }

    private static IList? EnsureStringList(object owner, string propName, object? current)
    {
        if (current is IList list)
            return list;

        var newList = new List<string>();
        TrySetMemberValue(owner, propName, newList);
        TrySetMemberValue(owner, propName.ToLowerInvariant(), newList);
        return newList;
    }

    private static void TrySetMemberValue(object obj, string name, object? value)
    {
        try
        {
            var type = obj.GetType();
            var prop = type.GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop is { CanWrite: true })
            {
                prop.SetValue(obj, value);
            }
            else
            {
                var field = type.GetField(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(obj, value);
                }
            }
        }
        catch
        {
            // 忽略反射错误
        }
    }
}

// --- 数据模型 ---

internal class WeaponSlotCompatWrapper
{
    public WeaponSlotCompatConfig? SalcosCompat { get; set; }
}

internal class WeaponSlotCompatConfig
{
    public string Id { get; set; } = "";
    public List<WeaponSlotOverride>? SlotOverrides { get; set; }
}

internal class WeaponSlotOverride
{
    public string SlotName { get; set; } = "";
    public List<string>? FilterTpls { get; set; }
    public bool ClearExcludedFilter { get; set; }
}
