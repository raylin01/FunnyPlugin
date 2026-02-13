using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Funnies.Commands;

namespace Funnies.Modules;

public static class Economy
{
    public const int MaxSupportedMoney = 65535;

    private static readonly Dictionary<int, int> GrenadeBuysBySlot = [];
    private static int _currentRound;

    public static int CurrentRound => _currentRound;

    public static HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        GrenadeBuysBySlot.Clear();

        if (IsWarmupRound())
        {
            _currentRound = 0;
            return HookResult.Continue;
        }

        var roundFromRules = GetRoundFromGameRules();
        _currentRound = roundFromRules ?? Math.Max(1, _currentRound + 1);

        if (Globals.Config.SpecialPlayerRoundMoneyEnabled && IsConfiguredMoneyRound(_currentRound))
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

        GrenadeBuysBySlot.Remove(player!.Slot);
        return HookResult.Continue;
    }

    public static HookResult OnItemPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!Util.IsPlayerValid(player)) return HookResult.Continue;

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

        GrenadeBuysBySlot[player.Slot] = GrenadeBuysBySlot.GetValueOrDefault(player.Slot) + 1;

        return HookResult.Continue;
    }

    public static HookResult OnBuyCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Util.IsPlayerValid(caller)) return HookResult.Continue;
        if (!Globals.Config.LimitNonSpecialGrenadeBuys) return HookResult.Continue;
        if (Util.IsSpecialPlayer(caller!)) return HookResult.Continue;

        var grenadeLimit = Math.Max(0, Globals.Config.NonSpecialGrenadeBuyLimit);
        var currentGrenadeBuys = GrenadeBuysBySlot.GetValueOrDefault(caller.Slot);
        if (currentGrenadeBuys < grenadeLimit) return HookResult.Continue;

        var attemptedItem = GetFirstArgument(command.ArgString);
        if (!IsGrenadeWeapon(attemptedItem)) return HookResult.Continue;

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
        Globals.Plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        Globals.Plugin.RegisterEventHandler<EventItemPurchase>(OnItemPurchase);
        Globals.Plugin.AddCommandListener("buy", OnBuyCommand);

        Globals.Plugin.AddCommand("css_specialmoney", "Configures special player round money rules", CommandEconomy.OnSpecialMoneyCommand);
        Globals.Plugin.AddCommand("css_nadelimit", "Configures grenade buy limit for non-special players", CommandEconomy.OnNadeLimitCommand);
        Globals.Plugin.AddCommand("css_ak", "Gives an AK-47 to the wallhacker/invisible player", CommandAk.OnAkCommand);
    }

    public static void Cleanup()
    {
        GrenadeBuysBySlot.Clear();
        _currentRound = 0;
    }
}
