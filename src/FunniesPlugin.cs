using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using Funnies.Commands;
using Funnies.Modules;

namespace Funnies;

public class FunniesConfig : BasePluginConfig
{
    public Funnies.Models.GameSetup GameSetup { get; set; } = new();
    [JsonPropertyName("DebugDamage")] public bool DebugDamage { get; set; } = false;
    [JsonPropertyName("ColorR")] public byte R { get; set; } = 171;
    [JsonPropertyName("ColorG")] public byte G { get; set; } = 75;
    [JsonPropertyName("ColorB")] public byte B { get; set; } = 209;
    [JsonPropertyName("CommandPermission")] public string AdminPermission { get; set; } = "@css/generic";
    [JsonPropertyName("RconPermission")] public string RconPermission { get; set; } = "@css/rcon";
    [JsonPropertyName("SpecialPlayerRoundMoneyEnabled")] public bool SpecialPlayerRoundMoneyEnabled { get; set; } = true;
    [JsonPropertyName("SpecialPlayerRoundMoneyAmount")] public int SpecialPlayerRoundMoneyAmount { get; set; } = 65535;
    [JsonPropertyName("SpecialPlayerMoneyRoundStartFirstHalf")] public int SpecialPlayerMoneyRoundStartFirstHalf { get; set; } = 2;
    [JsonPropertyName("SpecialPlayerMoneyRoundEndFirstHalf")] public int SpecialPlayerMoneyRoundEndFirstHalf { get; set; } = 12;
    [JsonPropertyName("SpecialPlayerMoneyRoundStartSecondHalf")] public int SpecialPlayerMoneyRoundStartSecondHalf { get; set; } = 14;
    [JsonPropertyName("SpecialPlayerMoneyRoundEndSecondHalf")] public int SpecialPlayerMoneyRoundEndSecondHalf { get; set; } = 24;
    [JsonPropertyName("LimitNonSpecialGrenadeBuys")] public bool LimitNonSpecialGrenadeBuys { get; set; } = true;
    [JsonPropertyName("NonSpecialGrenadeBuyLimit")] public int NonSpecialGrenadeBuyLimit { get; set; } = 2;
    [JsonPropertyName("DisableSkinsServerWide")] public bool DisableSkinsServerWide { get; set; } = false;
    [JsonPropertyName("DisableGloveSkinsServerWide")] public bool DisableGloveSkinsServerWide { get; set; } = false;
}
 
[MinimumApiVersion(373)]
public class FunniesPlugin : BasePlugin, IPluginConfig<FunniesConfig>
{
    public override string ModuleName => "Funny plugin";
    public override string ModuleVersion => "0.0.1";

    public FunniesConfig Config { get; set; } = new();

    public override void Load(bool hotReload)
    {
        Console.WriteLine("So funny :)");

        Globals.Plugin = this;

        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);
        RegisterListener<Listeners.OnTick>(OnTick);

        AddCommand("css_money", "Gives a player money", CommandMoney.OnMoneyCommand);
        AddCommand("css_rcon", "Runs a command", CommandRcon.OnRconCommand);
        AddCommand("css_start", "Starts the live game", CommandMatch.OnStartCommand);
        AddCommand("css_pause", "Pauses the match", CommandMatch.OnPauseCommand);
        AddCommand("css_unpause", "Unpauses the match", CommandMatch.OnUnpauseCommand);
        AddCommand("css_stop", "Returns match to warmup", CommandMatch.OnStopCommand);
        AddCommand("css_rr", "Restarts the round", CommandMatch.OnRestartRoundCommand);
        AddCommand("css_map", "Changes map", CommandMatch.OnMapCommand);
        AddCommand("css_skins", "Configures server-wide weapon skin suppression", CommandVisuals.OnSkinsCommand);
        AddCommand("css_gloves", "Configures server-wide glove skin suppression", CommandVisuals.OnGlovesCommand);

        #if DEBUG
        AddCommand("css_debug", "Debug command", CommandDebug.OnDebugCommand);
        #endif

        Wallhack.Setup();
        Invisible.Setup();
        GloveSuppression.Setup();
        MissPenalty.Setup();
        Economy.Setup();
        GameSession.Setup();
    }

    public override void Unload(bool hotReload)
    {
        Economy.Cleanup();
        MissPenalty.Clear();
        Invisible.Cleanup();
        GloveSuppression.Restore();
        Wallhack.Cleanup();
    }

    public void OnTick()
    {
        Wallhack.OnTick();
        GloveSuppression.OnTick();
        Invisible.OnTick();
        MissPenalty.OnTick();
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
