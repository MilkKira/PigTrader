using SPTarkov.Server.Core.Models.Spt.Mod;
using Range = SemanticVersioning.Range;
using Version = SemanticVersioning.Version;

namespace PigTrader_Server;

public sealed record MetaData : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.Neko.pigTrader";
    public override string Name { get; init; } = "PIGTRADER";
    public override string Author { get; init; } = "Milk_kira";
    public override List<string>? Contributors { get; init; } = [];
    public override Version Version { get; init; } = new("1.0.0");
    public override Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; } = [];
    public override Dictionary<string, Range>? ModDependencies { get; init; } = [];
    public override string? Url { get; init; } = "https://github.com/";
    public override bool? IsBundleMod { get; init; } = false;
    public override string License { get; init; } = "MIT";
}
