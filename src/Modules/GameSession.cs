using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace Funnies.Modules;

public static class GameSession
{
    public static bool HandleLegacy(CCSPlayerController? caller, CommandInfo command, string operation)
    {
        if (!State.Enabled) return false;
        if (Access(caller)) Execute(operation == "map" ? new[] { operation, command.GetArg(1) } : new[] { operation }, command.ReplyToCommand);
        return true;
    }
    private static Models.GameSetup State => Globals.Config.GameSetup;
    private static readonly HashSet<uint> Assigned = new();
    private static string Id(CCSPlayerController p) => p.IsBot ? $"bot:{p.PlayerName}" : p.SteamID.ToString();
    private static bool Access(CCSPlayerController? p) => p == null || AdminManager.PlayerHasPermissions(p, Globals.Config.AdminPermission);
    private static void Save(Action<string> reply)
    {
        if (!ConfigPersistence.TryPersist(out var detail)) reply($"Applied, but could not save: {detail}");
    }

    private static void ApplyRole(CCSPlayerController p)
    {
        var role = State.Roles.GetValueOrDefault(Id(p), "none");
        if (role is "wallhack" or "both") Globals.Wallhackers.Add(p.Slot);
        else Globals.Wallhackers.Remove(p.Slot);
        if (role is "invis" or "both") Globals.InvisiblePlayers.TryAdd(p, new());
        else Globals.InvisiblePlayers.Remove(p);
    }

    private static void AssignTeam(CCSPlayerController p)
    {
        var team = (CsTeam)State.TeamFor(Id(p));
        if (p.Team == team) return;
        if (team == CsTeam.Spectator) p.ChangeTeam(team);
        else p.SwitchTeam(team);
    }

    private static void Sync()
    {
        if (!State.Enabled) return;
        foreach (var p in Util.GetValidPlayers())
        {
            ApplyRole(p);
            // Only place newcomers during setup. Never undo an admin's manual move,
            // or fight the engine's halftime/overtime side swaps during a live match.
            if (!State.Live && p.Team >= CsTeam.Terrorist && Assigned.Add(p.EntityHandle.Raw))
            {
                // A spectator who explicitly joins a playing team is opting back in.
                if (State.TeamOverrides.GetValueOrDefault(Id(p)) == 1) State.TeamOverrides.Remove(Id(p));
                AssignTeam(p);
            }
        }
    }

    private static void Overtime()
    {
        Server.ExecuteCommand($"mp_overtime_enable {(State.Overtime ? 1 : 0)}");
        if (State.Overtime)
        {
            Server.ExecuteCommand("mp_overtime_maxrounds 6");
            Server.ExecuteCommand("mp_overtime_startmoney 10000");
        }
    }

    public static void Setup()
    {
        Assigned.Clear();
        Globals.Plugin.AddCommand("css_game", "Match setup menu and commands", Command);
        Globals.Plugin.RegisterEventHandler<EventPlayerDisconnect>((e, _) => {
            if (e.Userid != null) Assigned.Remove(e.Userid.EntityHandle.Raw);
            return HookResult.Continue;
        });
        Globals.Plugin.AddTimer(0.5f, Sync, TimerFlags.REPEAT);
        Globals.Plugin.RegisterListener<Listeners.OnMapStart>(_ => {
            Assigned.Clear();
            if (!State.Enabled) return;
            State.Live = false;
            Globals.Plugin.AddTimer(1f, () => {
                if (!State.Enabled) return;
                Lobby();
                Save(Console.WriteLine);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    private static void Lobby()
    {
        Server.ExecuteCommand("mp_autoteambalance 0");
        Server.ExecuteCommand("mp_limitteams 0");
        Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
        Server.ExecuteCommand("mp_warmup_pausetimer 1");
        Server.ExecuteCommand("mp_warmup_start");
        Overtime();
    }

    private static void Command(CCSPlayerController? caller, CommandInfo command)
    {
        if (!Access(caller)) { command.ReplyToCommand("You do not have match setup permission."); return; }
        var args = Enumerable.Range(1, command.ArgCount - 1).Select(command.GetArg).ToArray();
        if (args.Length == 0 && caller != null) { Menu(caller); return; }
        Execute(args, command.ReplyToCommand);
    }

    private static CCSPlayerController? Find(string value, Action<string> reply)
    {
        var matches = Util.GetValidPlayers().Where(p => string.Equals(p.PlayerName, value, StringComparison.OrdinalIgnoreCase)
            || $"#{p.Slot}" == value || Id(p) == value).ToArray();
        if (matches.Length == 1) return matches[0];
        reply("Player not found or name ambiguous. Use a quoted full name or #slot from !game status.");
        return null;
    }

    private static void Execute(string[] a, Action<string> reply)
    {
        var op = a.FirstOrDefault()?.ToLowerInvariant() ?? "status";
        if (op == "status")
        {
            reply($"Economy: {State.SpecialEconomy}; ordinary-player grenade purchases: {(Globals.Config.LimitNonSpecialGrenadeBuys ? Globals.Config.NonSpecialGrenadeBuyLimit.ToString() : "off")} per round.");
            reply($"Setup: {(State.Live ? "LIVE" : "lobby")}; special default: {(State.SpecialTeam == 2 ? "T" : "CT")}; overtime: {(State.Overtime ? "on (MR3, $10000)" : "off")}; managed: {State.Enabled}");
            foreach (var p in Util.GetValidPlayers()) reply($"#{p.Slot} {p.PlayerName}: {State.Roles.GetValueOrDefault(Id(p), "none")}, current {p.Team}");
            return;
        }
        if (op == "overtime" && a.Length == 2 && a[1] is "on" or "off")
        {
            State.Overtime = a[1] == "on";
            Overtime(); Save(reply); reply($"Overtime {a[1]}."); return;
        }
        if (op is "nades" or "nade" && a.Length == 2)
        {
            if (a[1] == "off") Globals.Config.LimitNonSpecialGrenadeBuys = false;
            else if (int.TryParse(a[1], out var limit) && limit >= 0)
            {
                Globals.Config.LimitNonSpecialGrenadeBuys = true;
                Globals.Config.NonSpecialGrenadeBuyLimit = limit;
            }
            else { reply("Usage: !game nades <nonnegative count|off>"); return; }
            Save(reply);
            reply($"Ordinary-player grenade purchase limit: {a[1]} (all types combined, per round).");
            return;
        }
        if (op == "economy" && a.Length == 2 && a[1] is "full" or "regular")
        {
            State.SpecialEconomy = a[1];
            Server.ExecuteCommand("cash_team_bonus_shorthanded 0");
            Server.ExecuteCommand("mp_maxmoney 16000");
            Save(reply);
            reply($"Special-player economy: {a[1]}. Takes effect next round; full grants $16000 except regulation pistol rounds, regular adds no cash.");
            return;
        }
        if (op is "stop" or "setup")
        {
            State.Enabled = true; State.Live = false;
            Lobby(); Sync(); Save(reply); reply("Setup open. Choose roles, then adjust teams before starting."); return;
        }
        if (State.Live)
        {
            reply("Use !game stop before changing the setup. Overtime can be changed while live."); return;
        }
        if (op == "map" && a.Length == 2)
        {
            var map = a[1].StartsWith("de_") || a[1].StartsWith("cs_") ? a[1] : $"de_{a[1]}";
            if (!System.Text.RegularExpressions.Regex.IsMatch(map, "^[a-zA-Z0-9_]+$") || !Server.IsMapValid(map))
            { reply("Unknown map. Use an installed map such as mirage or de_dust2."); return; }
            State.Enabled = true; Save(reply);
            Server.ExecuteCommand($"changelevel {map}"); return;
        }
        if (op == "role" && a.Length == 3 && a[2] is "wallhack" or "invis" or "both" or "none")
        {
            var p = Find(a[1], reply); if (p == null) return;
            State.Enabled = true;
            if (a[2] == "none") State.Roles.Remove(Id(p)); else State.Roles[Id(p)] = a[2];
            ApplyRole(p);
            // Explicit manual team overrides take precedence over the simple defaults.
            if (p.Team >= CsTeam.Terrorist) AssignTeam(p);
            Assigned.Add(p.EntityHandle.Raw);
            Sync(); Save(reply); reply($"{p.PlayerName}: {a[2]}."); return;
        }
        if (op == "team" && a.Length == 3 && a[2] is "t" or "ct" or "spec")
        {
            var p = Find(a[1], reply); if (p == null) return;
            State.Enabled = true;
            State.TeamOverrides[Id(p)] = a[2] == "t" ? 2 : a[2] == "ct" ? 3 : 1;
            if (a[2] == "spec") p.ChangeTeam(CsTeam.Spectator); else AssignTeam(p);
            Assigned.Add(p.EntityHandle.Raw);
            Save(reply); reply($"{p.PlayerName} moved to {a[2]}."); return;
        }
        if ((op == "side" && a.Length == 2 && a[1] is "t" or "ct") || op == "autoteams")
        {
            State.Enabled = true;
            if (op == "side") State.SpecialTeam = a[1] == "t" ? 2 : 3;
            State.TeamOverrides.Clear();
            foreach (var p in Util.GetValidPlayers().Where(p => p.Team >= CsTeam.Terrorist))
            { AssignTeam(p); Assigned.Add(p.EntityHandle.Raw); }
            Save(reply); reply("Automatic teams applied. You can now move individual players."); return;
        }
        if (op == "start")
        {
            if (!Util.GetValidPlayers().Any(p => State.Roles.ContainsKey(Id(p)) && p.Team >= CsTeam.Terrorist))
            { reply("Choose at least one participating special player first."); return; }
            if (!Util.GetValidPlayers().Any(p => p.Team == CsTeam.Terrorist) ||
                !Util.GetValidPlayers().Any(p => p.Team == CsTeam.CounterTerrorist))
            { reply("Both T and CT need at least one player before starting."); return; }
            // Capture the actual lobby teams, including moves made with other admin tools.
            foreach (var p in Util.GetValidPlayers().Where(p => p.Team >= CsTeam.Terrorist)) State.TeamOverrides[Id(p)] = (int)p.Team;
            State.Enabled = true; State.Live = true;
            Sync(); CompetitiveRules.Apply(); Overtime(); Save(reply);
            Server.ExecuteCommand("mp_autoteambalance 0");
            Server.ExecuteCommand("mp_limitteams 0");
            Server.ExecuteCommand("mp_ignore_round_win_conditions 0");
            Server.ExecuteCommand("mp_unpause_match");
            Server.ExecuteCommand("mp_warmup_pausetimer 0");
            Server.ExecuteCommand("mp_warmup_end");
            Server.ExecuteCommand("mp_restartgame 1");
            reply("Game started with your current teams."); return;
        }
        reply("!game [setup|map <map>|role <player> wallhack/invis/both/none|side t/ct|team <player> t/ct/spec|autoteams|overtime on/off|status|start|stop]");
    }

    private static void RunMenu(CCSPlayerController p, params string[] args)
    {
        if (!Access(p)) return;
        Execute(args, p.PrintToChat);
        Menu(p);
    }

    private static void Menu(CCSPlayerController p)
    {
        if (!Access(p)) return;
        var menu = new ChatMenu($"Game setup: {(State.Live ? "LIVE" : "lobby")}");
        if (!State.Live) menu.AddMenuOption("Open setup / warmup", (v, _) => RunMenu(v, "setup"));
        menu.AddMenuOption("Choose map", (viewer, _) => {
            var maps = new ChatMenu("Map");
            foreach (var map in new[] { "mirage", "dust2", "inferno", "nuke", "ancient", "anubis", "vertigo" })
                maps.AddMenuOption(map, (v, _) => RunMenu(v, "map", map));
            MenuManager.OpenChatMenu(viewer, maps);
        });
        menu.AddMenuOption("Player roles / manual teams", (viewer, _) => {
            var players = new ChatMenu("Choose player");
            foreach (var target in Util.GetValidPlayers())
            {
                var id = Id(target);
                players.AddMenuOption($"{target.PlayerName}: {State.Roles.GetValueOrDefault(id, "none")} ({target.Team})", (v, _) => {
                    var choices = new ChatMenu(target.PlayerName);
                    foreach (var role in new[] { "wallhack", "invis", "both", "none" })
                        choices.AddMenuOption($"Role: {role}", (owner, _) => RunMenu(owner, "role", id, role));
                    foreach (var team in new[] { "t", "ct", "spec" })
                        choices.AddMenuOption($"Move to {team}", (owner, _) => RunMenu(owner, "team", id, team));
                    MenuManager.OpenChatMenu(v, choices);
                });
            }
            MenuManager.OpenChatMenu(viewer, players);
        });
        menu.AddMenuOption($"Auto teams: specials {(State.SpecialTeam == 2 ? "T" : "CT")} (switch)", (v, _) => RunMenu(v, "side", State.SpecialTeam == 2 ? "ct" : "t"));
        menu.AddMenuOption("Reset manual moves / apply auto teams", (v, _) => RunMenu(v, "autoteams"));
        menu.AddMenuOption($"Overtime: {(State.Overtime ? "on" : "off")} (toggle)", (v, _) => RunMenu(v, "overtime", State.Overtime ? "off" : "on"));
        menu.AddMenuOption($"Special economy: {State.SpecialEconomy} (toggle)", (v, _) => RunMenu(v, "economy", State.SpecialEconomy == "full" ? "regular" : "full"));
        menu.AddMenuOption($"Grenade buys: {(Globals.Config.LimitNonSpecialGrenadeBuys ? Globals.Config.NonSpecialGrenadeBuyLimit.ToString() : "off")}", (v, _) => {
            var limits = new ChatMenu("Total grenade purchases per round");
            foreach (var count in new[] { "off", "0", "1", "2", "3", "4" })
                limits.AddMenuOption(count, (owner, _) => RunMenu(owner, "nades", count));
            limits.AddMenuOption("Custom: !game nades <number>", (_, _) => { }, true);
            MenuManager.OpenChatMenu(v, limits);
        });
        menu.AddMenuOption("Show setup", (v, _) => RunMenu(v, "status"));
        menu.AddMenuOption(State.Live ? "Stop / return to setup" : "Start with current teams", (v, _) => RunMenu(v, State.Live ? "stop" : "start"));
        MenuManager.OpenChatMenu(p, menu);
    }
}
