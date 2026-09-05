# Admin game setup

Type `!game` for the chat menu. Access uses `CommandPermission` (default `@css/generic`); unrestricted RCON is not required. Console equivalents use `css_game`.

1. Open setup/warmup and choose a map.
2. Select players and give each wallhack, invisibility, both, or no role.
3. Apply automatic teams: all special roles on T by default, everyone else on CT. Spectators stay spectators. You can switch the default side.
4. Make manual moves with the player menu (T, CT, spectator) or other admin tools.
5. Check the roster, set overtime, and Start. Start keeps the current teams.

Example (quote names containing spaces):

```text
!game setup
!game map mirage
!game role "Ray Lin" wallhack
!game role Alex both
!game side t
!game team Alex ct
!game overtime on
!game status
!game start
```

`!game side t/ct` and `!game autoteams` clear manual overrides and rebuild default teams. Apply these before individual moves. Repeating a role assignment leaves it enabled. `!game role Alex none` removes both powers. Names must match a single player; `#slot` from `!game status` or a SteamID also selects a player.

Overtime defaults to enabled: six rounds per overtime (three per side), $10,000 per overtime half. `!game overtime off` disables it. You can change this while live. In Full economy, special players receive $16,000 on non-pistol rounds, including overtime; in Regular economy they use the normal overtime starting money. Start restores MR12 competitive rules: 24 regulation rounds, halftime, 1:55 rounds, 15-second freeze time, $800 starting money, and 20-second buying restricted to buy zones. Bots move and shoot. Your chosen teams, bot roster, powers, and overtime toggle are retained.

Use `!game stop` to return to setup before changing roles, maps, or teams. While managed setup is enabled, legacy `!start`, `!stop`, and `!map` route through it; legacy role toggles explain how to use `!game role`. Existing pause/unpause commands remain available.

Roles, overtime, default side, and manual overrides are saved in plugin JSON. Human roles use SteamIDs and return after reconnects and map changes. Bots use names; newly named bots are ordinary players. Map changes return to warmup. Live teams are not continuously forced, allowing engine halftime and overtime swaps. Live reconnects restore powers but use normal server team-join behavior. During setup, new participants get their default team or saved override. Spectators can opt back in by joining a playing team.

Local startup now defaults to competitive play. Use Start-TestServer.ps1 -RegressionMode only for stationary-bot debugging; !game start restores competitive settings.

Grenade purchases: use !game nades <number> or !game nades off (also available in the menu). This is one combined per-player, per-round limit across grenade types, for players without special roles. Zero blocks all grenade purchases. It counts successful purchases, including freeze time; throwing or dropping grenades does not restore the budget. Pickups are not purchases. Existing !nadelimit commands edit the same persisted setting. The default remains configurable in NonSpecialGrenadeBuyLimit; Start does not overwrite it.

Economy: !game economy full gives only special players $16,000 once at the start of each non-pistol round (during freeze time, before buying); purchases are not refunded or topped up. !game economy regular disables all special cash grants. Changes apply next round. The menu toggles the same persisted setting. Managed games disable shorthanded bonus income and cap money at $16,000. Ordinary players retain normal competitive economy in both modes.

Full economy excludes both regulation pistol rounds (1 and 13 under MR12); those rounds use normal starting money. Overtime receives the full grant. Regular mode never adds special cash.
