# Easy Red 2 Realism Overhaul

*A configurable overhaul for more believable combined-arms battles.*

ER2RealismOverhaul is built around the rough edges that become hard to ignore after enough hours in Easy Red 2: bayonets missing at arm's length, attacking squads stalling in the open, defenders wandering out of good positions, troops riding an APC into obvious danger, and AI firing through friendlies.

The goal is to fix those moments without replacing the game underneath them. This is not a health or damage multiplier mod; Easy Red 2's missions, armour system, and basic damage model remain intact.

> **Current release:** 1.0.5
>
> **Compatibility:** Tested with Easy Red 2 Steam public-branch build `24246380` (July 20, 2026)

## What's new in 1.0.5

- **Large battles no longer accumulate the mod's catastrophic managed-GC stalls.** Short-lived interop objects are reclaimed progressively, expensive AI work is bounded and spread across frames, and both collectors use measured headroom instead of surprising a busy frame. The final maximum-AI validation contained none of the original 200-500 ms managed stalls and only 0.57 frames above 33 ms per 30 seconds.
- **AI in your own squad is completely native again.** Every overhaul AI system now leaves player squadmates to Easy Red 2 and your orders, including when you switch soldiers or ride in an AI-driven tank. Other allied squads and enemy AI retain the overhaul.
- **The optional flight model is strictly player-only and off by default.** It adds momentum, stalls, spins, energy loss, speed-dependent controls, damage-sensitive handling, and optional instruments only to aircraft you personally fly with realistic controls. AI and simplified controls remain native.
- **Aircraft freelook keeps you in control.** While the right stick moves your view, the left stick can continue pitching and rolling the aircraft; low-speed rudder authority now also reflects propeller slipstream while becoming appropriately heavy at high speed.
- **A lethal headshot now cuts immediately to black and silence.** The death panel and skip prompt remain visible, other deaths are unchanged, and the view and sound restore automatically when you regain control.
- **Moving squads no longer abandon soldiers indefinitely in cover.** Combat halts are bounded whenever the squad still has somewhere to go, while genuine defensive holds remain indefinite.
- **Multiplayer clients now receive the host's settings and complete destroyed-soldier cleanup correctly**, avoiding silent local-setting divergence and stale client squad members.

## What it fixes and improves

### Infantry combat

- **Melee attacks connect at believable distances.** The base hit check is longer and slightly wider for both players and AI, greatly reducing point-blank ghost swings. At the default setting, an ordinary strike reaches roughly 1.32 m and a bayonet roughly 1.68 m. Damage is unchanged.
- **AI no longer snaps onto every target or tracks enemies forever.** Soldiers need time to visually acquire a target, have a limited forward field of view, and lose firing permission when a remembered target is no longer reasonably known.
- **Cover is treated as a position, not a suggestion.** Exposed infantry look for threat-facing cover, favour trenches and lower stances, avoid piling multiple soldiers into the same spot, and hold good cover instead of constantly shuffling away from it. Cover rays use the penetration model's material and measured thickness, so foliage, glass, canvas, and thin props no longer receive the same survival value as earthworks, sandbags, masonry, or substantial buildings.
- **Suppression changes behaviour.** Pinned soldiers get low, exposed troops may crawl toward safety, mounted gunners duck and stop firing, and heavily suppressed squads see and remember less. Nearby allied wounds and deaths now add a separate morale shock to AI only, with deaths having the larger radius and effect.
- **Movement and weapon handling are less awkward.** Riflemen stop before firing, SMGs retain limited close-range moving fire, exposed AI reload from a safer posture, and crawling soldiers must stop before reloading or bandaging.
- **Squads can suppress a position without seeing through walls.** A stationary machine gunner may fire one short burst at a fresh, personally confirmed last-known position, using real ammunition and without tracking an invisible target.

### Offensive and defensive AI

- **Squads and vehicles attack and defend under the game's own routing.** Easy Red 2's maps are balanced around vanilla's continuous frontal pressure, so the mod no longer runs its own attack/defense operation planner; the tactical layer below still governs how soldiers fight once native orders send them into position.
- **Attacks are less likely to die after first contact.** Assaulting squads use buildings, trenches, and other strong cover as intermediate bounds or support-by-fire positions, then resume their advance once covering fire is established or the maximum combat halt is reached. A modest configurable attack-posture bonus helps offensive AI maintain pressure without changing health or damage.
- **Defenders build their plan around fortified ground.** The AI groups nearby cover into positions, values protection and firing lanes above raw distance, and reserves distinct slots. After arriving, each defender takes one useful trench or building position—or holds the arrival point when none is free—and stays put unless that position is destroyed or unsafe.
- **Static weapons are a core part of every defence.** Defenders immediately attempt to crew every viable gun in the objective area while preserving leaders, key specialists, donor-squad strength, and a complete mobile reserve. Reported armour makes AP-capable guns the first staffing priority. Empty armed troop transports parked in the position, such as a halftrack with a mounted machine gun, are crewed the same way; tanks, assault guns, and aircraft are not.
- **AI-led transports dismount before disaster.** Infantry leave APCs when credible nearby contact or incoming fire makes remaining inside the greater risk, rather than waiting for the vehicle to be destroyed.

### Weapons, vehicles, and battlefield effects

- **AI has better fire discipline.** Handheld and mounted weapons check for friendlies in the firing lane. Grenades require sensible range, a clear target area, and a per-soldier cooldown.
- **Tanks behave more like armoured vehicles.** They hold useful fighting distances, reverse without exposing their rear, avoid pointless hull pivots, accelerate with more weight, and keep attacking when their orders call for pressure.
- **Infantry respond more sensibly to armour.** Riflemen hold their small-arms fire against a tank instead of plinking at it, anti-tank troops wait for a practical launcher shot rather than firing a rocket across the map, and crews on valid defensive gun assignments do not abandon their weapons because a separate suppression reaction tried to dismount them.
- **Thin cover is no longer automatically bulletproof.** Material-aware penetration measures cover thickness and resistance, carries reduced projectile energy through suitable props, and preserves entry, exit, tracer, decal, and ricochet feedback. Terrain, bunkers, vehicle armour, and native armour penetration remain meaningful.
- **Optional flight physics for your own aircraft.** Off by default. When enabled, planes you fly gain momentum, progressive stalls and spins, energy loss through hard manoeuvring, speed-dependent control authority, and damage-sensitive handling, with an optional compact instrument HUD. AI aircraft are never affected.
- **Soldiers flinch when they shoot.** The game's own recoil system was never fed for anyone but the soldier you control, so riflemen fired without their posture moving at all. Every other soldier now takes a weapon kick scaled from that weapon's recoil stat. Cosmetic by default; an optional switch also lets recoil disturb their aim.
- **Freelook no longer costs you the turn.** Holding the vehicle freelook button used to leave your aircraft with rudder only. The left stick now flies the plane for as long as you look — pitch and roll — while the right stick moves your head. Works in either stick layout and whether or not the flight model is enabled; rudder and throttle stand down on that stick only while you are actually manoeuvring.
- **Explosions have more presence without simply inflating every damage value.** Artillery missions, bomb effects, fragmentation, suppression, smoke, dust, craters, and small-explosion ragdoll force are reworked separately so each can be tuned on its own.

### Smaller fixes and presentation options

- More restrained AI command gestures and better animation quality for visible distant soldiers.
- Allied multiplayer infantry can deliberately form one squad: open the player list, select a player, and choose **Join [player]'s squad**. Joining is per life and never happens automatically on spawn or respawn.
- Configurable impact-decal lifetime, tracer frequency, battle chatter, distant sound shaping, weapon audio, tank engines, tracks, player footsteps, and rain/snow particle size, amount, and fall speed.
- Adjustable player suppression effects—including a larger near-miss radius and optional depth-of-field blur—plus true 10x Caps Lock binocular zoom with the weapon model hidden, 200-degree hold-Alt freelook, first-person shadows, and allied multiplayer names when the rest of the HUD is hidden.
- A bullet to the head that kills you cuts to black and silence instantly instead of leaving the death camera on your body, with the death panel still readable over it; every other death is unchanged, and sight and sound always return when you take control of a soldier again.
- A map-north-aligned scrolling bottom-screen compass tape shown for five seconds with **K**, using NATO mils by default with optional degree bearings, tapered fading edges, and an option to keep it permanently visible.
- A built-in settings menu with Apply, Cancel, reset controls, individual switches for nearly every system, and one-click Enable All / Disable All system kill switches.

Press **F10**, or choose **Realism Overhaul Settings** from the main or pause menu. AI options are organized into Attack Posture Bonuses, Defense, Infantry Tactics, Vehicle Tactics, Support Coordination, and Diagnostics. Defender static-weapon staffing has its own switch on the Defense page. Non-AI settings are unchanged.

The F10 menu also lets you rebind the **Binoculars Key**, **Free Look Key**, and **Compass Key**: select a binding, press the desired key, then Apply to save it. Defaults remain **Caps Lock**, either **Alt** key, and **K**.

On foot in first person, press **Caps Lock** to toggle the clean 10x binocular view and hold either **Alt** key for a 200-degree freelook arc. Press **K** during gameplay to show the scrolling compass band for five seconds. The F10 menu includes magnification, freelook arc, compass units, and always-visible compass settings.

### Visual AI diagnostics

Press **F8** during gameplay to show the local visual AI debug layer. It is disabled by default, does not issue orders or alter synchronized AI state, and only collects its event feed and timing counters while visible. The Diagnostics page in the F10 menu can change the toggle key, startup state, sampling distance, maximum infantry count, and event-history duration.

- **F7** freezes or resumes the current snapshot.
- **F6** cycles all, allied, and enemy AI immediately, including while the snapshot is frozen. The header shows the player-faction reference and visible/total actor counts so the active filter is auditable.
- **\\** focuses the soldier nearest the center of the screen; **[** and **]** cycle sampled soldiers; **Backspace** clears focus.
- **1-9** selects one clearly labelled layer: actors, perception, movement, fire safety, danger, command/contact, vehicles, support, or events/performance. Hold **Shift** while pressing **1-9** to combine or remove layers. **0** returns to the minimal Actors view; **Shift+0** shows everything.
- **-** and **=** reduce or increase the temporary viewing distance by 50 m. **Delete** clears captured events and timing history.

The header always names the selected layer, its color, and the meaning of its marks. Unfocused views are deliberately sparse; focusing a soldier opens the detailed FOV, candidates, movement ownership, cover geometry, safety state, or matching command traffic. The cyan movement route is only Easy Red 2's live executor destination. Tactical winners and planned cover are labelled separately, so proposal context can never masquerade as the route the soldier is actually following.

The complete layer and marker reference is in [`docs/AI_VISUAL_DEBUG.md`](docs/AI_VISUAL_DEBUG.md).

## Coming soon

Tank combat is the next major area being rebuilt. Tank ballistics, armour values, and internal vehicle subsystems are all being reworked for a future release. These changes are still work in progress and are not part of v1.0.5; for now, the mod leaves Easy Red 2's existing tank ballistics and armour model alone.

## Installation

### Requirements

- Easy Red 2 on Windows.
- **BepInEx 6 for Unity IL2CPP, Windows x64.** Download a build whose filename contains `Unity.IL2CPP-win-x64` from the [official BepInEx builds page](https://builds.bepinex.dev/projects/bepinex_be). The [official IL2CPP installation guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) is available if this is your first BepInEx mod.
- The latest `ER2RealismOverhaul.dll` from the [Releases page](../../releases/latest).

### Setup

1. In Steam, right-click **Easy Red 2** and select **Manage > Browse local files**.
2. Extract BepInEx into the game folder, beside `Easy Red 2.exe`.
3. Start the game once, then close it. The first launch may take longer while BepInEx generates its files.
4. Open `BepInEx\plugins` and create a folder named `ER2RealismOverhaul`.
5. Place `ER2RealismOverhaul.dll` inside that folder.
6. Start the game and press **F10** to confirm the mod is loaded.

Your installation should look like this:

```text
Easy Red 2
|-- Easy Red 2.exe
`-- BepInEx
    `-- plugins
        `-- ER2RealismOverhaul
            `-- ER2RealismOverhaul.dll
```

Do not place the DLL in `Easy Red 2_Data`.

### Updating

Close the game and replace the existing `ER2RealismOverhaul.dll` with the new one. Your settings remain in `BepInEx\config\ca.antoi.er2.tacticalai.cfg`.

### Uninstalling

Close the game and delete `ER2RealismOverhaul.dll`. Delete the configuration file above as well if you want to remove your saved settings.

## Multiplayer

Only the host needs the mod for its host-authoritative gameplay systems. Players without BepInEx or ER2RealismOverhaul can join normally and experience the battlefield state that Easy Red 2 synchronizes from the host, including the results of the host-controlled AI behaviour.

Unmodded players do not receive the F10 menu, the host's settings, or local presentation features such as HUD, audio, and first-person options. Players using the same mod version receive the host's settings for the session; their menu remains read-only and their own configuration returns after disconnecting.

To join another player's infantry squad, the joining player must have the mod installed. Open the multiplayer player list, select an allied player, and click **Join [player]'s squad** on that player's profile. This moves only your current soldier; your former AI squadmates remain in their original squad. Vehicle crews are excluded, and respawning does not automatically put you back into the shared squad.

If the original host leaves and an unmodded player becomes host, the overhaul can no longer run its host-authoritative systems. For the best results, a player with the same mod version should remain host.

## Compatibility and feedback

Easy Red 2 updates can change the game code that BepInEx mods rely on. If the mod stops loading after a game update, or something behaves differently online than it does offline, please report it on the project's [Issues page](../../issues).

## License

ER2RealismOverhaul is **source-available, not open source**. Personal, non-commercial use and private modification are permitted. Republishing, re-uploading, redistributing, selling, including the mod in a mod pack, or releasing modified copies is not permitted. See the [ER2RealismOverhaul Personal Use License](LICENSE) for the complete terms.
