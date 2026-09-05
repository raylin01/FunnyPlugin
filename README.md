# Funny Plugin

## Overview
This plugin recreates the 1v5 but one person has wallhacks and the invisible man gamemode shown in dima_wallhacks and renyan videos for Counter Strike 2.

## Installation
This branch requires **.NET 10** and **CounterStrikeSharp 1.0.373 or newer** (use the with-runtime distribution for a new server).

1. Install [Counter Strike Sharp](https://docs.cssharp.dev/docs/guides/getting-started.html) on your server.
2. Download the plugin from the [releases](https://github.com/Name2781/FunnyPlugin/releases) and put it in `server/game/csgo/addons/counterstrikesharp/plugins/Funnies`.

## Usage:

### Commands:
Note: Make sure you have the `@css/generic` permission otherwise you wont be able to use commands. https://docs.cssharp.dev/docs/admin-framework/defining-admins.html

1. `!wallhack <player name>` gives a player wallhacks.
2. `!invisible <player name>` toggles invisibility. Player, weapons, and attached models share a fade timeline; fully faded entities are filtered from transmission. Custom glove/material behavior still requires client-side visual verification; see [testing notes](docs/TESTING.md).
3. `!ak` gives the wallhacker/invisible player an AK-47 (works on either team).

### Admin Commands:
1. `!rcon <command>` runs a server command.
2. `!money <amount> <player name>` gives a player money.
3. `!start` ends warmup, unpauses, and restarts into live play.
4. `!pause` pauses the match (technical pause).
5. `!unpause` resumes from technical pause.
6. `!stop` puts the server back into warmup.
7. `!rr` restarts the current round.
8. `!map <mapname>` changes the current map.
9. `!specialmoney show` shows the current special-player round money settings.
10. `!specialmoney enabled <0|1>` toggles special-player round money automation.
11. `!specialmoney amount <money>` sets how much money special players get on configured rounds (runtime cap is 65535 due game limits).
12. `!specialmoney rounds <start1> <end1> <start2> <end2>` sets money round windows.
13. `!nadelimit show` shows current grenade-buy-limit settings.
14. `!nadelimit enabled <0|1>` toggles non-special grenade limit.
15. `!nadelimit limit <count>` sets the max grenade buys per round for non-special players.
16. `!skins show` displays current server-wide weapon-skin suppression state.
17. `!skins enabled <0|1>` toggles server-wide weapon-skin suppression.

Note: `!specialmoney` and `!nadelimit` also persist to the plugin JSON config file.

## Contact:
If you have any issues, feedback, or feature requests please make an issue on the issues page. If you want to contact me directly about making custom plugins you can through discord (namethempguy).

## License:
This plugin is licensed under the MIT License. Feel free to use, modify, and distribute it in your servers. Attribution is appreciated but not required.
### Invisibility cosmetic settings

Use `!gloves enabled 1` (admin) or `css_gloves enabled 1` (server console) to disable glove skins for every player. The setting persists as `DisableGloveSkinsServerWide: true`. Owners also see default gloves; agent and weapon skins remain enabled. `!gloves enabled 0` restores tracked gloves. CounterStrikeSharp's `FollowCS2ServerGuidelines` must be `false` for the glove item updates.

To hide the enemy name shown under the crosshair, set `mp_playerid 1` in your server/gamemode cfg. Teammate identification remains enabled; `mp_playerid 2` hides all target names instead.
