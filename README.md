# Funny Plugin

Counter-Strike 2 server modes with wallhack and invisible-player roles. Admins can assign powers to multiple players, arrange uneven teams, and run competitive matches with configurable economy, overtime, and grenade purchases.

## Installation

Requires **.NET 10**, **Metamod:Source**, and **CounterStrikeSharp 1.0.373 or newer**. Use CounterStrikeSharp's with-runtime distribution when the server does not already have its required runtime.

1. Install CounterStrikeSharp on the CS2 server.
2. Build the plugin with `dotnet build Funnies.csproj -c Release`, or use `./scripts/Build.ps1` on Windows.
3. Put the build output from `bin/Release/net10.0/` into `game/csgo/addons/counterstrikesharp/plugins/Funnies/`.
4. Load the plugin and configure admin access. Its settings are stored in `game/csgo/addons/counterstrikesharp/configs/plugins/Funnies/Funnies.json`.

The diagnostic plugin under `tests/ServerDriver` is a separate local testing tool, not a required server component.

## Admin permissions

The `CommandPermission` setting defaults to `@css/generic`. An admin with this permission can use the entire `!game` menu, including maps, roles, teams, economy, overtime, and match start/stop. It also controls the plugin's general money, grenade-limit, and cosmetic commands. Server console commands are allowed.

Add the actual player's SteamID64 to CounterStrikeSharp's `configs/admins.json`:

```json
{
  "Match admin": {
    "identity": "REPLACE_WITH_YOUR_STEAMID64",
    "flags": ["@css/generic"]
  }
}
```

Run `css_admins_reload` in the server console after editing. `css_admins_list` shows the loaded identities and flags. Ordinary players cannot change the match setup.

`RconPermission` defaults to `@css/rcon` and controls unrestricted `!rcon`, pause/unpause, and `!rr`. The managed `!game` setup does not require unrestricted RCON.

## Set up a match

Type **`!game`** to open the chat menu:

1. Open setup/warmup and choose the map.
2. Select one or more players and assign **wallhack**, **invis**, **both**, or **none**.
3. Apply automatic teams: special players on T by default, ordinary players on CT. Choose CT instead to reverse the sides. Spectators stay spectators.
4. Move individual players to T, CT, or spectator as needed.
5. Choose economy, overtime, and the grenade-purchase limit.
6. Review the setup and Start. **Start preserves the current teams**, including manual moves made with other admin tools.

Example: Mirage with two special players starting on T:

```text
!game setup
!game map mirage
!game role "Player One" wallhack
!game role "Player Two" invis
!game side t
!game economy full
!game overtime on
!game nades 2
!game status
!game start
```

To place Player Two on the opposite team, use `!game team "Player Two" ct` before Start.

### Setup commands

| Command | Behavior |
|---|---|
| `!game` | Open the admin menu |
| `!game setup` | Enter setup/warmup |
| `!game map mirage` | Load an installed map; `de_mirage` also works |
| `!game role <player> wallhack/invis/both/none` | Set a player's powers; repeating the assignment keeps the same role |
| `!game side t/ct` | Set the default special side and rebuild automatic teams |
| `!game autoteams` | Clear manual overrides and rebuild automatic teams |
| `!game team <player> t/ct/spec` | Move a player during setup |
| `!game economy full/regular` | Select special-player economy; applies next round |
| `!game overtime on/off` | Enable or disable overtime |
| `!game nades <count>` | Set ordinary players' combined grenade purchases per round |
| `!game nades off` | Disable the plugin's purchase cap |
| `!game status` | Show roles, current teams, economy, overtime, and grenade limit |
| `!game start` | Apply competitive rules and start with the current teams |
| `!game stop` | Return to setup/warmup |

Quote full names containing spaces. Use `#slot` from `!game status` or a SteamID when names are ambiguous. Console commands replace the `!` prefix with `css_`, for example `css_game overtime on`.

Apply automatic teams **before** manual moves: `side` and `autoteams` intentionally clear overrides. Stop the match before changing maps, roles, or teams. Economy, overtime, and grenade settings can be changed while live.

Settings and human roles persist in JSON using SteamIDs. Roles return after reconnects and map changes; a map change returns the match to warmup. Bot roles use bot names. During setup, joining participants receive their default team or saved override. Live team placement follows normal server joining behavior, and the plugin allows normal halftime/overtime side swaps.

## Competitive rules and economy

Start applies MR12 competitive settings: 24 regulation rounds, halftime, about 1:55 per round, a 15-second freeze period, a 20-second buy window, buy-zone-only purchases, normal respawning rules, and moving/shooting bots. Automatic balancing stays disabled to preserve the chosen uneven teams. The existing bot roster is retained.

| Economy | Special players | Ordinary players |
|---|---|---|
| **Full** | $16,000 once at the start of each non-pistol round, before buying | Normal competitive economy |
| **Regular** | Normal competitive economy; no extra cash | Normal competitive economy |

Full is the default. **Both regulation pistol rounds (1 and 13) are excluded** and use normal starting money. Purchases are not refunded or continuously topped up. Money is capped at $16,000, and shorthanded-team bonus income is disabled.

Overtime defaults to enabled: six rounds per overtime, three per side, with $10,000 starting money per half. Full economy grants special players $16,000 in overtime; Regular follows normal overtime economy.

## Grenade purchase limit

The cap is configurable, with a default of **2 total grenade purchases per ordinary player per round**. It combines flashbangs, HE grenades, smoke grenades, Molotovs/incendiaries, and decoys rather than applying a separate allowance to each type.

- `!game nades 1`: one total purchase per round.
- `!game nades 3`: three total purchases per round.
- `!game nades 0`: no grenade purchases.
- `!game nades off`: no plugin purchase cap; normal game carry limits still apply.

Only successful purchases count. Freeze-time purchases count toward that same round. Throwing or dropping a grenade does not refill the allowance; picking up a dropped grenade is not a purchase. Rejections happen before payment/item creation. **Players with wallhack or invisibility are exempt.**

`!nadelimit show`, `!nadelimit enabled 0/1`, and `!nadelimit limit <count>` edit the same settings: `LimitNonSpecialGrenadeBuys` and `NonSpecialGrenadeBuyLimit`.

## Roles and miss penalties

Wallhack displays other living players' outlines to the assigned viewer. Colors follow health: green at 100 HP, yellow at 70, orange at 40, and red at 10. A viewer retains their own wallhack while spectating; this does not grant powers to the observed player.

Invisible players reveal and fade in response to game actions. Body, weapons, and supported attachments follow the fade timeline. Fully faded entities are hidden from other clients. Use glove suppression below to avoid custom gloves lingering during the fade.

A player without invisibility incurs miss penalties while a living invisible opponent exists. Successful hits do not charge a miss penalty; the target still takes normal engine damage.

| Missed attack | HP penalty |
|---|---:|
| Pistol, SMG, rifle, machine gun | 2 |
| Knife (primary or secondary), shotgun | 5 |
| HE/Molotov | 5 after the damaging lifetime ends without a successful hit |
| Sniper rifle | 8 |

Special players can use `!ak` to receive an AK-47 on either team.

## Global cosmetic overrides

Glove and weapon suppression are **separate settings**, apply server-wide, and persist in plugin JSON. Neither changes the player's Steam inventory or ownership.

### Gloves

```text
!gloves show
!gloves enabled 1
!gloves enabled 0
```

`enabled 1` means **enable suppression**: everyone uses default gloves, including the player's own first-person view. Agent skins and weapon finishes remain intact. There is no owner-only custom-glove mode.

The plugin clears the equipped glove cosmetic on the player pawn, sends the updated fields to clients, and restores the default glove bodygroup. While enabled it suppresses subsequently applied glove cosmetics too. It saves the current pawn's glove definition/initialization state and restores tracked gloves when suppression is disabled or the plugin unloads.

This is the visually verified workaround for custom gloves that remain opaque while the body fades. Set `FollowCS2ServerGuidelines` to `false` in CounterStrikeSharp's `configs/core.json`, then run `css_core_reload` before enabling suppression. The plugin reports the prerequisite instead of changing it automatically.

Config key: `DisableGloveSkinsServerWide` (default `false`).

### Weapons

```text
!skins show
!skins enabled 1
!skins enabled 0
```

Weapon suppression attempts to reset carried weapons' paint/finish fields on spawn, pickup, and periodic checks. It does not disable agents or gloves. Compatibility depends on the weapon fields exposed by the CounterStrikeSharp/game version; this path has not received the same client visual validation as glove suppression.

Disabling it stops further resets. **Original weapon finishes are not saved/restored** by this override; a fresh item or another cosmetic provider may need to reapply them.

Config key: `DisableSkinsServerWide` (default `false`). For the glove-fade workaround while keeping weapon skins, use `!gloves enabled 1` and leave `!skins enabled 0`.

### Enemy target names

`mp_playerid 1` hides enemy names under the crosshair while retaining teammate identification. `mp_playerid 2` hides both. This does not hide scoreboard, chat, or spectator names. Match Start applies `mp_playerid 1`; place the setting in your server/gamemode cfg as well.

## Other commands

| Command | Purpose |
|---|---|
| `!start`, `!stop`, `!map <map>` | Route through match setup while it is enabled |
| `!pause`, `!unpause` | Pause/resume the match (`RconPermission`) |
| `!rr` | Restart the match with `mp_restartgame 1` (`RconPermission`) |
| `!money <amount> <exact player name>` | Set a player's money (`CommandPermission`) |
| `!rcon <command>` | Execute a server command (`RconPermission`) |

Outside managed setup, `!wh`/`!wallhack` and `!invis`/`!invisible` toggle one exact player name. Managed games use `!game role` instead. Likewise, `!specialmoney show/enabled/amount/rounds` configures the standalone round-money rules; managed games use `!game economy full/regular`.

## Development and testing

See [Admin setup](docs/ADMIN-SETUP.md), [Testing](docs/TESTING.md), and [Glove rendering research](docs/GLOVE-RESEARCH.md) for operational details and validation coverage.

Windows helpers:

```powershell
./scripts/Setup-TestServer.ps1
./scripts/Build.ps1
./scripts/Test.ps1
./scripts/Start-TestServer.ps1
```

Local startup uses competitive play. `Start-TestServer.ps1 -RegressionMode` explicitly selects the stationary-bot, long-round debugging profile. `!game start` restores competitive settings.

## Support and license

Report issues in the repository's issue tracker. Licensed under the [MIT License](LICENSE).
