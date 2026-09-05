# Local build and regression environment

## Requirements and layout

- .NET 10 SDK (the session installed SDK 10.0.400 in the workspace's sibling `.dotnet` directory).
- CS2 Windows game files; a separate dedicated-server copy is used.
- Metamod 2.0 build 1411 and CounterStrikeSharp **with-runtime** 1.0.373.
- Plugin targets .NET 10 / CounterStrikeSharp API 1.0.373. Older CounterStrikeSharp installations must be upgraded before deployment.

From this repository, run:

```powershell
./scripts/Setup-TestServer.ps1
./scripts/Build.ps1
./scripts/Test.ps1
./scripts/Start-TestServer.ps1
```

Setup accepts `-GamePath` and `-ServerRoot`; startup accepts `-ServerRoot`. Defaults use the Steam installation and a sibling `.test-server` directory. Framework archives are pinned and SHA-256 checked. The SDK and NuGet cache are local to the workspace/repository. The server binds to **127.0.0.1:27016**, with LAN mode, cheats, stationary bots, and VAC disabled for local testing. The original Steam game files are not modified by server setup. A full game copy needs substantial free space; the session's existing copy includes about 162 GiB, including local content. Future setup copies exclude demos, dumps, and logs.

Client launch options for testing:

```text
-insecure -windowed -w 1280 -h 720 -console +connect 127.0.0.1:27016
```

Otherwise, enter `connect 127.0.0.1:27016` in the client console. The server PID is saved in `.test-server/server.pid`; stdout/stderr and framework logs are retained under that directory. Stop the dedicated server before rerunning setup/start. `gamemode_competitive_server.cfg` in the test copy applies the test settings after Valve's mode defaults.

The current session's local Steam account has `@css/generic` in the test copy's `admins.json`, allowing the role commands. That machine-specific file is not part of the repository.

## Test driver

`tests/ServerDriver` is a separate diagnostic plugin, excluded from the Funnies build. Build its `TestDriver.csproj`, then copy `TestDriver.*` from `bin/Release/net10.0` into the test server's `addons/counterstrikesharp/plugins/TestDriver` directory. It polls a local `commands.txt` in its own plugin directory and executes each line in the server console. It is a local test fixture, not part of the release plugin.

Useful commands:

```text
test_dump
test_glow
test_hp <slot> <health>
test_pos <slot> <x> <y> <z> <yaw>
test_give <slot> <weapon_designer_name>
test_equip <slot> <weapon_designer_name>
test_aim <bot_slot> <yaw> <pitch>
test_hold <bot_slot> <button_mask> <seconds>
test_after <seconds> <server_command>
test_enterfire <bot_slot> <seconds_after_next_inferno_spawns>
test_team <slot> <team_number>
test_reveal <slot> <total_reveal_seconds>
```

`test_hold` injects bot button state: `1` is primary attack and `2048` is secondary. Set `bot_dont_shoot 0` when exercising combat. Health/equip/position commands create controlled fixtures; the successful knife and grenade tests used real engine weapon attacks, not synthetic `player_hurt` events. For grenades, give/equip the grenade, hold primary attack briefly, and release.

`test_reveal` sends a `player_sound` event through the normal reveal handler for a repeatable visual fade check. It does not simulate physical bot movement; use actual movement afterward to check animation-dependent cosmetics.

Use `css_invis <exact bot name>` to enable the target role (it prints enabled/disabled), and `css_wh <exact player name>` for the viewer. `DebugDamage: true` in Funnies' JSON config logs charged penalties. The production default is false.

## Results from 2026-09-05

Build: Debug and Release succeed with zero warnings and zero errors. The standalone regression executable passes 17 checks for attack matching, callback ordering, duplicate knife observations, independent projectiles, late burns, death/round cancellation, and fade boundaries.

| Scenario | Observed result |
|---|---|
| Right-click knife hit | Invisible bot lost 65 HP; attacker stayed at 100 HP |
| Left-click knife hit | Invisible bot lost 25 HP; attacker stayed at 100 HP |
| Held right-click misses | One 5-HP penalty per actual swing, including repeated swings |
| HE grenade hit | Invisible bot lost 16 HP; attacker stayed at 100 HP |
| AK-47 hit | Invisible bot lost 35 HP; attacker stayed at 100 HP |
| Molotov miss | One 5-HP penalty after the damaging lifetime ended |
| Late Molotov hit | Bot entered fire six seconds after ignition, received five 8-HP burn ticks; attacker stayed at 100 HP |
| Five round restarts | Four live bots retained exactly four glow entities per snapshot; new entity handles and health colors updated each round |
| Health color boundaries | 70 HP: `FFFFFF00`; 40 HP: `FFFFA500`; 10 HP: `FFFF0000` (ARGB) |
| Native damage-result compatibility | Knife/HE paths supplied a null result pointer; paired pre/post health observations handled them without replacing engine damage |

Session evidence: sibling `.test-server/evidence/attack-tests.log` and `glow-round-tests.log`. These contain server logs and local player identifiers, so they are not checked into the repository.

## Visual validation

### Glove suppression and enemy target names

`DisableGloveSkinsServerWide` is now implemented separately from weapon skin suppression. Set it to `true`, or use admin command `!gloves enabled 1` (`css_gloves enabled 1` from the server). `!gloves enabled 0` restores tracked cosmetics; `!gloves show` reports the setting. It applies to all players, including their own first-person gloves, and retains weapon/agent skins. It requires CounterStrikeSharp `FollowCS2ServerGuidelines=false`; the command reports that prerequisite instead of silently changing core settings. Glove fields are restored on disable/unload and newly applied cosmetics are suppressed while enabled.

Live implementation checks passed: enable cleared bot and human glove definitions; disable restored 5030/initialized state; reapplication was suppressed; JSON persistence and automatic plugin reload retained the enabled setting. Glock paint 38 and the agent model remained intact. Delayed cosmetic bodygroup updates are corrected while suppression is active. Both build configurations pass with zero warnings/errors, and the existing 17 regression checks pass. Further full-match testing with arbitrary third-party cosmetic plugins remains advisable.

The test server also uses `mp_playerid 1`, verified against the running engine's help: show teammate target names only. Use `mp_playerid 2` to suppress all target names. This controls the crosshair target identifier, not scoreboard, chat, or spectator labels. The setting is saved in the test/gamemode server cfg. For a separate production server, put `mp_playerid 1` in its server/gamemode cfg.

The connected user returned to their living CT player, aimed at Trapper, and confirmed that the red enemy name label was gone.

Fallback verified in the connected client: explicitly networking the cleared glove-item definition and initialized leaf fields, restoring the default-glove bodygroup, and removing experimental wearables allowed Bloody Darryl's default gloves and Glock Fade to fade with the body in three cycles. Owner-only custom glove rendering did not pass. See [GLOVE-RESEARCH.md](GLOVE-RESEARCH.md) for the distinction and current prototype status.

The connected human viewer confirmed that the default agent's body, default gloves, and pistol faded together across three reveal cycles. The viewer also confirmed that the Bloody Darryl agent, Vice gloves, and Glock Fade fixture render correctly before fading. Applying the paint to an existing Glock did not refresh its visible finish; recreating it and applying the paint immediately after giving it resolved that fixture issue. **Custom glove fading still fails:** Vice gloves remain visible during the body's fade and snap off at its end. Both forced alpha transparency and normal pawn render mode failed the visual check. The server model inventory contains no separate glove model for the normal pawn cosmetic. The server-owned wearable experiment failed and was removed; global glove-skin suppression is the verified fallback.

For the custom cosmetic fixture, set `FollowCS2ServerGuidelines` to `false` in the isolated test server's CounterStrikeSharp `configs/core.json`, then run `css_core_reload`. The Windows-only test driver command `test_skins <bot slot>` applies Bloody Darryl The Strapped, Sport Gloves Vice (5030/10048), and Glock Fade (4/38). `test_cosmetics` reports the applied server state. The native attribute setter signature and cosmetic identifiers were checked against Nereziel/cs2-WeaponPaints; a future CS2 update may require updating that test signature. This fixture is separate from the shipped plugin.

The experimental `test_glove_entity <bot slot>` replaces the pawn's client-generated gloves with a server `wearable_item`. Asset metadata confirms that `glove_sporty` bodygroup 0 is the third-person mesh, while Bloody Darryl bodygroup 1 hides the agent's default gloves. **This experiment failed:** although the stationary fixture briefly looked correct, subsequent reveal cycles switched to default gloves before revealing opaque Vice gloves that snapped off at the end. Do not use this as a production fix. `test_glove_clear <bot slot>` removes the experimental wearable and restores saved pawn glove fields. The live experiment was removed and the normal cosmetic setup restored.

Diagnostic `test_fade_state <slot>` confirmed that, halfway through the four-second reveal cycle, the pawn, server wearable, Glock, and knife all had alpha 127 and `kRenderTransAlpha`. The server wearable was present in the pawn's child traversal. This rules out a missing server attachment or mismatched server alpha for that experiment; it does not identify the precise client rendering failure. No working smooth custom-glove fade has been established.

Windows computer-use capture failed repeatedly with `SetIsBorderRequired failed: No such interface supported (0x80004002)`. The user supplied the visual observations: custom-glove fading failed, default-glove fading passed, and wallhack colors passed. Server-side alpha, render mode, entity lifetime, and color state alone cannot establish that custom glove materials fade correctly on the client.

With a viewer and an invisible opponent using the affected gloves:

1. Observe the opponent moving, stopping, shooting, and reloading. Body, gloves, weapons, and attachments should follow the same fade and be fully absent at its end.
2. Toggle invisibility off and drop/pick up weapons. Original tint, opacity, render mode, and shadows should return.
3. Enable wallhack on the viewer; change the opponent's HP to 100, 70, 40, and 10. Confirm green/yellow/orange/red through a wall.
4. Repeat after deaths, team changes, five rounds, and a plugin reload.

Additional integration cases worth exercising before a public release: overlapping real grenade throws, extinguished Molotovs, the affected custom glove cosmetics, and mixed plugins that also alter rendering or damage. The attack ledger has independent-projectile logic tests, but every such engine combination has not been tested here.

### Wallhack client validation completed

The connected user confirmed green/yellow/orange/red health outlines through cover, then confirmed that all five restart cycles worked. Server snapshots also showed updated glow entities and colors through the fifth cycle. During the sequence the user switched to spectator; wallhack remained assigned to the viewer account, not the observed bots. Evidence is retained locally in the sibling .test-server/evidence/wallhack-client-round-check.log. These were explicit match restarts; natural halftime transitions and mixed-plugin integration are separate follow-up cases.
