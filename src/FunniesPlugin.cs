using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using Funnies.Commands;
using Funnies.Modules;

namespace Funnies;

public class FunniesConfig : BasePluginConfig
{
    [JsonPropertyName("ColorR")] public byte R { get; set; } = 171;
    [JsonPropertyName("ColorG")] public byte G { get; set; } = 75;
    [JsonPropertyName("ColorB")] public byte B { get; set; } = 209;
    [JsonPropertyName("CommandPermission")] public string AdminPermission { get; set; } = "@css/generic";
    [JsonPropertyName("RconPermission")] public string RconPermission { get; set; } = "@css/rcon";
    [JsonPropertyName("SpecialPlayerRoundMoneyEnabled")] public bool SpecialPlayerRoundMoneyEnabled { get; set; } = true;
    [JsonPropertyName("SpecialPlayerRoundMoneyAmount")] public int SpecialPlayerRoundMoneyAmount { get; set; } = 999999;
    [JsonPropertyName("SpecialPlayerMoneyRoundStartFirstHalf")] public int SpecialPlayerMoneyRoundStartFirstHalf { get; set; } = 2;
    [JsonPropertyName("SpecialPlayerMoneyRoundEndFirstHalf")] public int SpecialPlayerMoneyRoundEndFirstHalf { get; set; } = 12;
    [JsonPropertyName("SpecialPlayerMoneyRoundStartSecondHalf")] public int SpecialPlayerMoneyRoundStartSecondHalf { get; set; } = 14;
    [JsonPropertyName("SpecialPlayerMoneyRoundEndSecondHalf")] public int SpecialPlayerMoneyRoundEndSecondHalf { get; set; } = 24;
    [JsonPropertyName("LimitNonSpecialGrenadeBuys")] public bool LimitNonSpecialGrenadeBuys { get; set; } = true;
    [JsonPropertyName("NonSpecialGrenadeBuyLimit")] public int NonSpecialGrenadeBuyLimit { get; set; } = 2;
}
 
public class FunniesPlugin : BasePlugin, IPluginConfig<FunniesConfig>
{
    public override string ModuleName => "Funny plugin";
    public override string ModuleVersion => "0.0.1";

    public FunniesConfig Config { get; set; }

    public override void Load(bool hotReload)
    {
        Console.WriteLine("So funny :)");

        Globals.Plugin = this;

        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnTick>(OnTick);

        AddCommand("css_money", "Gives a player money", CommandMoney.OnMoneyCommand);
        AddCommand("css_rcon", "Runs a command", CommandRcon.OnRconCommand);

        #if DEBUG
        AddCommand("css_debug", "Debug command", CommandDebug.OnDebugCommand);
        #endif

        Wallhack.Setup();
        Invisible.Setup();
        Economy.Setup();
    }

    public override void Unload(bool hotReload)
    {
        #if DEBUG
        if (hotReload)
        {
            Economy.Cleanup();
            Invisible.Cleanup();
            Wallhack.Cleanup();
        }
        #else
        Console.WriteLine($"Reloading: hotReload? {hotReload}");
        #endif
    }

    public void OnTick()
    {
        Wallhack.OnTick();
        Invisible.OnTick();
    }

    public void OnCheckTransmit(CCheckTransmitInfoList infoList)
    {
        foreach ((CCheckTransmitInfo info, CCSPlayerController? player) in infoList)
        {
            if (!Util.IsPlayerValid(player))
                continue;

            Wallhack.OnPlayerTransmit(info, player!);
            Invisible.OnPlayerTransmit(info, player!);
        }
    }

    public void OnConfigParsed(FunniesConfig config)
    {
        Config = config;
        Globals.Config = config;
    }
}
