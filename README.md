# Funny Plugin

## Overview
This plugin recreates the 1v5 but one person has wallhacks and the invisible man gamemode shown in dima_wallhacks and renyan videos for Counter Strike 2.

## Installation
1. Install [Counter Strike Sharp](https://docs.cssharp.dev/docs/guides/getting-started.html) on your server.
2. Download the plugin from the [releases](https://github.com/Name2781/FunnyPlugin/releases) and put it in `server/game/csgo/addons/counterstrikesharp/plugins/Funnies`.

## Usage:

### Commands:
Note: Make sure you have the `@css/generic` permission otherwise you wont be able to use commands. https://docs.cssharp.dev/docs/admin-framework/defining-admins.html

1. `!wallhack <player name>` gives a player wallhacks.
2. `!invisible <player name>` makes a player invisible. Note: have the invisible person take off their skins that have StatTrak or nametags on them otherwise they won't be hidden.
3. `!ak` gives the wallhacker/invisible player an AK-47 (works on either team).

### Admin Commands:
1. `!rcon <command>` runs a server command.
2. `!money <amount> <player name>` gives a player money.
3. `!specialmoney show` shows the current special-player round money settings.
4. `!specialmoney enabled <0|1>` toggles special-player round money automation.
5. `!specialmoney amount <money>` sets how much money special players get on configured rounds.
6. `!specialmoney rounds <start1> <end1> <start2> <end2>` sets money round windows.
7. `!nadelimit show` shows current grenade-buy-limit settings.
8. `!nadelimit enabled <0|1>` toggles non-special grenade limit.
9. `!nadelimit limit <count>` sets the max grenade buys per round for non-special players.

Note: `!specialmoney` and `!nadelimit` also persist to the plugin JSON config file.

## Contact:
If you have any issues, feedback, or feature requests please make an issue on the issues page. If you want to contact me directly about making custom plugins you can through discord (namethempguy).

## License:
This plugin is licensed under the MIT License. Feel free to use, modify, and distribute it in your servers. Attribution is appreciated but not required.
