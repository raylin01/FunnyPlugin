using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;

namespace Funnies.Modules;

public static class CompetitiveRules
{
    public static void Apply()
    {
        ConVar.Find("bot_stop")?.SetValue(false);
        ConVar.Find("bot_dont_shoot")?.SetValue(false);
        ConVar.Find("sv_infinite_ammo")?.SetValue(0);
        // Restore competitive play without executing the stock cfg, which also
        // changes bot quota and could remove players from an admin's chosen roster.
        foreach (var command in new[] {
            "mp_roundtime 1.92", "mp_roundtime_defuse 1.92", "mp_roundtime_hostage 1.92",
            "mp_maxrounds 24", "mp_halftime 1", "mp_match_can_clinch 1", "mp_timelimit 0",
            "mp_freezetime 15", "mp_buytime 20", "mp_buy_anywhere 0", "mp_startmoney 800",
            "mp_maxmoney 16000", "mp_afterroundmoney 0", "mp_free_armor 0",
            "cash_team_bonus_shorthanded 0",
            "mp_playercashawards 1", "mp_teamcashawards 1", "mp_friendlyfire 1",
            "mp_respawn_on_death_t 0", "mp_respawn_on_death_ct 0",
            "mp_ignore_round_win_conditions 0", "sv_cheats 0",
            "mp_autoteambalance 0", "mp_limitteams 0", "mp_playerid 1"
        }) Server.ExecuteCommand(command);
    }
}
