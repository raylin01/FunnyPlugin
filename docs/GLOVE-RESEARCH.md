# Custom glove fading and owner-only cosmetics

Research date: 2026-09-05. No production fix is established by this research.

## What the reports actually establish

- [CS2-AdminTools source](https://github.com/NeuTroNBZh/CS2-AdminTools/blob/main/src/Modules/Invisible.cs) applies pawn and weapon alpha and removes their transmission to other viewers when alpha reaches zero. Its advertised lingering-glove fix synchronizes final disappearance; it does not implement continuous custom-glove opacity. Adopting that endpoint logic would not solve the opaque-during-fade behavior reproduced here.
- [frier3n/cs2-invisible](https://github.com/frier3n/cs2-invisible/blob/main/invisible.cs) sets pawn and weapon color/shadows. It offers no distinct custom-glove fade mechanism.
- A [CS:GO developer report](https://forums.alliedmods.net/showthread.php?t=328483) describes gloves remaining after a player is hidden, including inventory-owned gloves. This is historical evidence of a related problem, not proof that Source 1 fixes work in current CS2.
- [kgns/gloves](https://github.com/kgns/gloves/blob/master/addons/sourcemod/scripting/gloves.sp) exposes `sm_gloves_enable_world_model` to control whether other living players see gloves. That repository targets CS:GO/SourceMod, not CS2/CounterStrikeSharp.

## What our local checks establish

Default gloves and the Glock Fade passed the observer's appearance/fade checks. Vice gloves stayed opaque or dark during the body fade and snapped off at its endpoint. A server wearable replacement failed too and introduced default/custom switching. Its server alpha was verified at 127 alongside pawn and weapons at 127, so the failed experiment was not simply missing attachment traversal. The replacement was removed.

The installed glove model uses separate first/third-person meshes. That provides a possible representation for an owner-only cosmetic, but does not prove that a server-created wearable will animate correctly in the owner's first-person view.

## Feasibility of hiding skins from others

| Approach | Owner keeps own cosmetic? | Current status |
|---|---|---|
| Clear the pawn's glove item and restore default bodygroups | Not guaranteed; changes shared pawn state | Straightforward suppression candidate, but does not meet the owner-view requirement by itself |
| Change shared glove/paint fields inside each viewer's transmit callback | Not a reliable implementation | Do not use; the callback filters entities, not separate cosmetic field values |
| Default third-person representation plus a cosmetic entity transmitted only to its owner | Potentially | Recommended bounded prototype; first-person attachment, animation and cleanup remain unverified |
| Rewrite individual cosmetic properties per network recipient in a native extension | Potentially | No ready-made maintained solution found in this search; much broader networking work |

[CounterStrikeSharp's transmit API](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Core.CCheckTransmitInfo.html) exposes entity transmission bitsets. It can filter a distinct cosmetic entity by viewer; it does not itself supply per-viewer replacements for fields within the same pawn or weapon. The feasibility judgments above are engineering inferences from that API and the local experiments, not claims of a published working CS2 solution.

## Next prototype and acceptance criteria

Start with gloves only: weapon skins do not need to be removed merely to address the glove failure. Keep default gloves in the shared pawn representation, and evaluate a separate first-person cosmetic restricted to its owner. Unlike the failed world-wearable experiment, other players would never receive that custom cosmetic and it would not need to fade.

1. Verify one clean custom pair in the owner's first-person view through idle, inspect, firing, reloading and weapon switching.
2. Verify a second observer sees default gloves, with the already-tested body fade.
3. Verify spectators, deaths, respawns, team changes, repeated rounds and plugin reload; restore original cosmetic state without duplicates.
4. Only after those pass, expose a server-wide option. Broader agent/weapon skin suppression is separate scope and needs its own view-model/world-model handling.

The existing `DisableSkinsServerWide` option currently targets weapon fallback fields through reflection. It does not implement owner-only cosmetics or complete glove/agent suppression and should not be represented as satisfying this request.

## Live owner-only prototype

`tests/ServerDriver/PrivateGloveFixture.cs` provides server-console-only `test_private_gloves <slot>` and `test_private_clear`. It saves the original pawn glove definition/initialized fields, clears the shared glove cosmetic, and creates a first-person Vice wearable whose transmission is restricted to the owner or an in-eye spectator of that owner. Cleanup removes the wearable and restores saved pawn state. This is a disposable test fixture, not enabled in the production plugin.

Initial server verification: viewer slot 4 receives its own fixture and is denied slot 0's fixture. First-person appearance, animation, default glove overlap and spectator views remain unverified. Transmission logs alone are not a visual pass.

Live visual follow-up: the owner-only fixture initially showed default/missing gloves on the living human player; applying normal pawn Vice gloves worked. After manually selecting Trapper in first-person spectator mode, the user reported seeing Vice gloves while the private fixture was active. These are distinct results: spectator visibility does not establish a pass for the living owner's view. The comparison from Specialist's view toward Trapper is pending.

Further checks invalidated that apparent spectator success: Specialist also saw Vice gloves on Trapper despite the private entity being filtered. The prototype was not explicitly marking the embedded glove item's definition and initialized fields as changed. After adding `SetStateChanged(pawn, "CCSPlayerPawn", "m_EconGloves", Schema.GetSchemaOffset("CEconItemView", field))` for both leaf fields and restoring the default bodygroup, Specialist saw default gloves. Trapper's own spectator view then also showed default/missing gloves. The earlier Vice appearance was retained normal cosmetic state, not a confirmed private wearable render.

The owner-only prototype therefore **fails** its first-person acceptance criterion. Its separate model has been removed from the live test. Shared default-glove suppression is now visually verified, but it also removes the owner's glove skin. After manually selecting Specialist and confirming readiness, the user observed three complete reveal cycles and confirmed that Trapper's default gloves, custom agent body, and Glock Fade all faded together and disappeared completely. This is a verified fallback for this fixture, not a solution that preserves owner-only glove skins. A production suppression option and lifecycle checks have not yet been implemented.
