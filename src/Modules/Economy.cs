using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public static class Economy
{
    public const int MaxSupportedMoney = 65535;

    private static readonly Dictionary<string, int> GrenadeBuysBySlot = [];
    private static string Buyer(CCSPlayerController p) => p.IsBot ? $"bot:{p.EntityHandle.Raw}" : p.SteamID.ToString();
    private static int _currentRound;

    public static int CurrentRound => _currentRound;

    public static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (IsWarmupRound())
        {
            _currentRound = 0;
            return HookResult.Continue;
        }

        var roundFromRules = GetRoundFromGameRules();
        _currentRound = roundFromRules ?? Math.Max(1, _currentRound + 1);

        if (!Globals.Config.GameSetup.Enabled && Globals.Config.SpecialPlayerRoundMoneyEnabled && IsConfiguredMoneyRound(_currentRound))
        {
            var specialMoney = GetEffectiveSpecialMoneyAmount();
            EnforceSpecialMoneyLimit(specialMoney);

            Globals.Plugin.AddTimer(0.1f, () =>
            {
                GrantSpecialPlayersMoney(specialMoney);
            });
        }

        return HookResult.Continue;
    }

    public static HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

        // Preserve human purchase counts through reconnects until the next round.
        return HookResult.Continue;
    }

    public static HookResult OnItemPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;
        if (Globals.Config.DebugDamage && IsGrenadeWeapon(NormalizeWeaponName(@event.Weapon ?? "")))
            Console.WriteLine($"[Funnies grenade purchase] slot={player.Slot} item={@event.Weapon} special={Util.IsSpecialPlayer(player)} used={GrenadeBuysBySlot.GetValueOrDefault(Buyer(player))} limit={Globals.Config.NonSpecialGrenadeBuyLimit}");

        if (ShouldTopUpSpecialMoney(player!))
        {
            var specialMoney = GetEffectiveSpecialMoneyAmount();
            Globals.Plugin.AddTimer(0.02f, () => GrantPlayerMoney(player, specialMoney));
        }

        if (!Globals.Config.LimitNonSpecialGrenadeBuys) return HookResult.Continue;
        if (Globals.Config.NonSpecialGrenadeBuyLimit < 0) return HookResult.Continue;
        if (Util.IsSpecialPlayer(player!)) return HookResult.Continue;

        var purchasedWeapon = NormalizeWeaponName(@event.Weapon ?? string.Empty);
        if (!IsGrenadeWeapon(purchasedWeapon)) return HookResult.Continue;

        GrenadeBuysBySlot[Buyer(player)] = GrenadeBuysBySlot.GetValueOrDefault(Buyer(player)) + 1;

        return HookResult.Continue;
    }

    private static HookResult OnCanAcquire(DynamicHook hook)
    {
        if (!Globals.Config.LimitNonSpecialGrenadeBuys || Globals.Config.NonSpecialGrenadeBuyLimit < 0) return HookResult.Continue;
        var method = hook.GetParam<AcquireMethod>(2);
        if (method is not (AcquireMethod.Buy or AcquireMethod.BuyWithCtrl)) return HookResult.Continue;
        var item = hook.GetParam<CEconItemView>(1);
        if (item.ItemDefinitionIndex is not (43 or 44 or 45 or 46 or 47 or 48 or 68)) return HookResult.Continue;
        var services = hook.GetParam<CCSPlayer_ItemServices>(0);
        var player = Util.GetValidPlayers().FirstOrDefault(p => p.PlayerPawn.Value?.ItemServices?.Handle == services.Handle);
        if (player == null || Util.IsSpecialPlayer(player)) return HookResult.Continue;
        if (GrenadeBuysBySlot.GetValueOrDefault(Buyer(player)) < Globals.Config.NonSpecialGrenadeBuyLimit) return HookResult.Continue;
        // Reject before money is charged or an item is created, including autobuy.
        hook.SetReturn(AcquireResult.ReachedGrenadeTotalLimit);
        return HookResult.Handled;
    }

    private static HookResult OnAcquireResult(DynamicHook hook)
    {
        if (!Globals.Config.DebugDamage) return HookResult.Continue;
        var item = hook.GetParam<CEconItemView>(1);
        if (item.ItemDefinitionIndex is not (43 or 44 or 45 or 46 or 47 or 48 or 68)) return HookResult.Continue;
        var services = hook.GetParam<CCSPlayer_ItemServices>(0);
        var player = Util.GetValidPlayers().FirstOrDefault(p => p.PlayerPawn.Value?.ItemServices?.Handle == services.Handle);
        if (player != null)
            Console.WriteLine($"[Funnies grenade acquire] slot={player.Slot} item={item.ItemDefinitionIndex} method={hook.GetParam<AcquireMethod>(2)} result={hook.GetReturn<AcquireResult>()} special={Util.IsSpecialPlayer(player)} used={GrenadeBuysBySlot.GetValueOrDefault(Buyer(player))} limit={Globals.Config.NonSpecialGrenadeBuyLimit}");
        return HookResult.Continue;
    }

    public static HookResult OnBuyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        return OnThrowableBuyCommand(caller, command, inspectArguments: true);
    }

    public static HookResult OnAutoBuyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        return OnThrowableBuyCommand(caller, command, inspectArguments: false);
    }

    private static HookResult OnThrowableBuyCommand(CCSPlayerController? caller, CommandInfo command, bool inspectArguments)
    {
        if (!Util.IsPlayerValid(caller)) return HookResult.Continue;
        if (!Globals.Config.LimitNonSpecialGrenadeBuys) return HookResult.Continue;
        if (Globals.Config.NonSpecialGrenadeBuyLimit < 0) return HookResult.Continue;
        if (Util.IsSpecialPlayer(caller!)) return HookResult.Continue;

        var grenadeLimit = Math.Max(0, Globals.Config.NonSpecialGrenadeBuyLimit);
        var currentGrenadeBuys = GrenadeBuysBySlot.GetValueOrDefault(Buyer(caller));
        if (currentGrenadeBuys < grenadeLimit) return HookResult.Continue;

        if (inspectArguments)
        {
            var attemptedItem = GetFirstArgument(command.ArgString);
            if (!IsGrenadeWeapon(attemptedItem)) return HookResult.Continue;
        }

        Util.ServerPrintToChat(caller, $"You can only buy {grenadeLimit} grenade(s) per round.");
        return HookResult.Handled;
    }

    private static void GrantSpecialPlayersMoney(int amount)
    {
        var money = Math.Clamp(amount, 0, MaxSupportedMoney);

        foreach (var player in Util.GetValidPlayers())
        {
            if (!Util.IsSpecialPlayer(player)) continue;
            GrantPlayerMoney(player, money);
        }
    }

    private static void GrantPlayerMoney(CCSPlayerController player, int amount)
    {
        if (!Util.IsPlayerValid(player)) return;
        if (player.InGameMoneyServices == null) return;

        var money = Math.Clamp(amount, 0, MaxSupportedMoney);
        player.InGameMoneyServices.Account = money;
        Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
    }

    private static int GetEffectiveSpecialMoneyAmount()
    {
        return Math.Clamp(Globals.Config.SpecialPlayerRoundMoneyAmount, 0, MaxSupportedMoney);
    }

    private static bool ShouldTopUpSpecialMoney(CCSPlayerController player)
    {
        if (Globals.Config.GameSetup.Enabled) return false;
        return Globals.Config.SpecialPlayerRoundMoneyEnabled &&
               IsConfiguredMoneyRound(_currentRound) &&
               Util.IsSpecialPlayer(player);
    }

    private static void EnforceSpecialMoneyLimit(int amount)
    {
        var money = Math.Clamp(amount, 0, MaxSupportedMoney);
        Server.ExecuteCommand($"mp_maxmoney {money}");
    }

    private static bool IsConfiguredMoneyRound(int round)
    {
        return IsRoundInRange(round, Globals.Config.SpecialPlayerMoneyRoundStartFirstHalf, Globals.Config.SpecialPlayerMoneyRoundEndFirstHalf) ||
               IsRoundInRange(round, Globals.Config.SpecialPlayerMoneyRoundStartSecondHalf, Globals.Config.SpecialPlayerMoneyRoundEndSecondHalf);
    }

    private static bool IsRoundInRange(int round, int start, int end)
    {
        if (start > end) (start, end) = (end, start);
        return round >= start && round <= end;
    }

    private static bool IsWarmupRound()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        return gameRules?.GameRules?.WarmupPeriod ?? false;
    }

    private static int? GetRoundFromGameRules()
    {
        var gameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault();
        if (gameRules?.GameRules == null) return null;

        var property = gameRules.GameRules.GetType().GetProperty("TotalRoundsPlayed");
        if (property?.GetValue(gameRules.GameRules) is not int totalRoundsPlayed) return null;

        return totalRoundsPlayed + 1;
    }

    private static string GetFirstArgument(string argString)
    {
        if (string.IsNullOrWhiteSpace(argString)) return string.Empty;

        var firstToken = argString.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return NormalizeWeaponName(firstToken ?? string.Empty);
    }

    private static string NormalizeWeaponName(string weaponName)
    {
        var normalized = weaponName.ToLowerInvariant();
        return normalized.StartsWith("weapon_") ? normalized["weapon_".Length..] : normalized;
    }

    private static bool IsGrenadeWeapon(string weaponName)
    {
        return weaponName.Contains("hegrenade") ||
               weaponName.Contains("flashbang") ||
               weaponName.Contains("smokegrenade") ||
               weaponName.Contains("molotov") ||
               weaponName.Contains("incgrenade") ||
               weaponName.Contains("decoy") ||
               weaponName.Contains("tagrenade");
    }

    public static void Setup()
    {
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnCanAcquire, HookMode.Pre);
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Hook(OnAcquireResult, HookMode.Post);
        Globals.Plugin.RegisterEventHandler<EventRoundPrestart>((_, _) => {
            GrenadeBuysBySlot.Clear();
            Globals.Plugin.AddTimer(0.2f, () => {
                if (Globals.Config.GameSetup is { Enabled: true, Live: true } setup && !IsWarmupRound())
                {
                    var amount = setup.RoundGrant(GetRoundFromGameRules() ?? 0, ConVar.Find("mp_maxrounds")?.GetPrimitiveValue<int>() ?? 24);
                    if (amount > 0) GrantSpecialPlayersMoney(amount);
                }
            }, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        });
        Globals.Plugin.RegisterListener<Listeners.OnMapStart>(_ => { GrenadeBuysBySlot.Clear(); _currentRound = 0; });
        Globals.Plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        Globals.Plugin.RegisterEventHandler<EventItemPurchase>(OnItemPurchase);
        Globals.Plugin.AddCommandListener("buy", OnBuyCommand, HookMode.Pre);
        // CanAcquire checks each autobuy/rebuy item without blocking gun purchases.

        Globals.Plugin.AddCommand("css_specialmoney", "Configures special player round money rules", CommandEconomy.OnSpecialMoneyCommand);
        Globals.Plugin.AddCommand("css_nadelimit", "Configures grenade buy limit for non-special players", CommandEconomy.OnNadeLimitCommand);
        Globals.Plugin.AddCommand("css_ak", "Gives an AK-47 to the wallhacker/invisible player", CommandAk.OnAkCommand);
    }

    public static void Cleanup()
    {
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnCanAcquire, HookMode.Pre);
        VirtualFunctions.CCSPlayer_ItemServices_CanAcquireFunc.Unhook(OnAcquireResult, HookMode.Post);
        GrenadeBuysBySlot.Clear();
        _currentRound = 0;
    }
}
