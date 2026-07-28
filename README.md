# Easy Red 2 Realism Overhaul

*A configurable overhaul for more believable combined-arms battles.*

ER2RealismOverhaul is built around the rough edges that become hard to ignore after enough hours in Easy Red 2: bayonets missing at arm's length, attacking squads stalling in the open, defenders wandering out of good positions, troops riding an APC into obvious danger, and AI firing through friendlies.

The goal is to fix those moments without replacing the game underneath them. This is not a health or damage multiplier mod; Easy Red 2's missions, armour system, and basic damage model remain intact.

> **Current release:** 1.0.8
>
> **Compatibility:** Tested with Easy Red 2 2.0.8 Stable, Steam public-branch build `24420558` (July 27, 2026)

## What's new in 1.0.8

- **The 1.0.5 large-battle stutter fix is back with a mixed-mod safety boundary.** Transient base-game wrappers no longer build an enormous managed finalizer backlog. UnityEngine wrappers and types injected by any plugin retain their normal Il2CppInterop lifetime behavior, and a base-game wrapper held by another mod remains live.
- **AI reacts promptly and shoots decisively in close quarters without becoming instant or omniscient.** A new nearby visible enemy can preempt an unrelated unconfirmed contact, then still requires the configured human reaction delay. Once confirmed, a close threat immediately halts a moving rifleman or machine gunner and releases fire permission; eligible close-range submachine guns can still fire while moving. The close-range accuracy advantage now remains meaningful through typical indoor fighting distances.
- **Confirmed contacts are shared locally instead of becoming faction-wide knowledge.** AI call out a frozen last-known target position to friendly AI within 15 m by default, adjustable from 5-50 m. A moving recipient does not turn or interrupt its route; after stopping, it can orient toward a still-fresh report but must personally see and acquire the target before firing.
- **Recovering from suppression no longer erases the danger immediately.** Direct incoming fire leaves a coarse last-known direction for at least 15 seconds without granting aim or fire permission against an unseen enemy. Precise visual target memory now defaults to 15 seconds and is adjustable from 5-30 seconds.
- **Pinned infantry keep a usable indoor firing posture.** A nearby solid ceiling keeps suppression-driven posture crouched instead of prone, while exposed soldiers outdoors can still flatten themselves.
- **Defenders occupy cover with less clustering.** Soldiers spread more strongly across equivalently protective firing positions, exact-position stacking is rejected more reliably in dense geometry, and clearly safer cover still takes priority.
- **Unsupported aiming develops recoverable native weapon sway.** Crouching lasts longer before fatigue sets in, while prone aiming or held breath provides support.
- **First-person shadows retain the third-person body silhouette.** Only first-person weapon and viewmodel renderers stop casting shadows, while the local soldier model continues to produce a natural body shadow.
- **Headshot deaths now use the native death-panel lifecycle.** The blackout remains headshot-only, sits behind the game's death information, mutes combat audio, and clears through the normal respawn and deployment flow.
- **The world HUD can be made less intrusive without weakening the map.** In-world marker sprites can be hidden while map markers remain visible. Nearby AI squad names and allied multiplayer names share the same camera-aware placement, with multiplayer labels retaining each player's online nickname. Each contextual name uses one clean gold draw without a duplicate black shadow label.
- **Dead multiplayer players can leave their current squad and redeploy.** A dedicated deployment-screen action uses the game's native squad-leave and respawn flow.
- **Ragdolls now inherit the soldier's real movement velocity.** Death momentum follows actual character movement and is clamped to keep the result stable.
- **Multiple machine guns no longer hit Easy Red 2's separate seven-loop sound cutoff.** The Audio page raises that limit to a configurable 24 automatic weapons by default, covering both handheld and mounted guns. The mod also raises Unity's local capacity on startup to at least 256 audible and 512 virtual voices without lowering higher native values.

## What's new in 1.0.6

- **Mixed-mod compatibility is restored.** Realism Overhaul no longer changes the lifetime of Unity objects belonging to other BepInEx plugins. This removes the global cleanup from early 1.0.5 builds that could cause `ObjectCollectedException` errors and prevent another mod from loading.

## What's new in 1.0.4

- **The AI aircraft systems are gone; the flight model is now yours alone.** AI attack safety, threat evasion, autonomous target hunting, patrol re-centering, and AI energy management are removed — AI planes fly and fight exactly as they do in vanilla Easy Red 2, under every setting. The flight model and instrument HUD survive as opt-in features for aircraft *you* pilot, both off by default. Aircraft-bomb blast, crater, and suppression tuning stays in the ordnance system.
- **The experimental AI commander is gone.** Easy Red 2's maps are balanced around continuous frontal pressure, and the commander's staged gating made attacks stall. Squads and vehicles now attack and defend under the game's own routing, with the infantry tactical layer unchanged.
- **Close-quarters fights are deadlier.** Point-blank threats are identified faster than merely close ones, AI weapon spread tightens as the target gets closer, and heavy suppression can no longer shrink a soldier's awareness of an immediate threat below a configurable minimum.
- **Shaped-charge warheads detonate on first contact.** Bazooka, Panzerschreck, Panzerfaust, and PIAT rounds no longer punch through fences and thin walls and re-appear as a fresh projectile on the far side.
- **AI defenders man abandoned vehicle guns.** A soldier will take the gunner seat of an empty armed transport parked in their defend area, and give it up as soon as a player claims the vehicle.

## What it fixes and improves

### Infantry combat

- **Melee attacks connect at believable distances.** The base hit check is longer and slightly wider for both players and AI, greatly reducing point-blank ghost swings. At the default setting, an ordinary strike reaches roughly 1.32 m and a bayonet roughly 1.68 m. Damage is unchanged.
- **AI no longer snaps onto every target or tracks enemies forever.** Soldiers need time to visually acquire a target, have a limited forward field of view, and lose firing permission when a remembered target is no longer reasonably known. Personally confirmed contacts can be called out to nearby friendly AI, but only as a short-lived last-known position that never grants aim or fire permission by itself. A soldier directly fired upon separately remembers the frozen direction of that danger long enough to avoid forgetting it as soon as suppression fades.
- **Cover is treated as a position, not a suggestion.** Exposed infantry look for threat-facing cover, favour trenches and lower stances, avoid piling multiple soldiers into the same spot, and hold good cover instead of constantly shuffling away from it. Cover rays use the penetration model's material and measured thickness, so foliage, glass, canvas, and thin props no longer receive the same survival value as earthworks, sandbags, masonry, or substantial buildings.
- **Suppression changes behaviour.** Pinned soldiers get low, exposed troops may crawl toward safety, and soldiers under a nearby solid ceiling remain crouched instead of losing their indoor firing lane by going prone. Mounted gunners duck and stop firing, and heavily suppressed squads see and remember precise contacts less reliably. Nearby allied wounds and deaths add a separate morale shock to AI only, with deaths having the larger radius and effect.
- **Movement and weapon handling are less awkward.** Riflemen stop before firing, SMGs retain limited close-range moving fire, exposed AI reload from a safer posture, and crawling soldiers must stop before reloading or bandaging.
- **Squads can suppress a position without seeing through walls.** A stationary machine gunner may fire one short burst at a fresh, personally confirmed last-known position, using real ammunition and without tracking an invisible target.

### Offensive and defensive AI

- **Squads and vehicles attack and defend under the game's own routing.** Easy Red 2's maps are balanced around vanilla's continuous frontal pressure, so the mod no longer runs its own attack/defense operation planner; the tactical layer below still governs how soldiers fight once native orders send them into position.
- **Attacks are less likely to die after first contact.** Assaulting squads use buildings, trenches, and other strong cover as intermediate bounds or support-by-fire positions, then resume their advance once covering fire is established or the maximum combat halt is reached. A modest configurable attack-posture bonus helps offensive AI maintain pressure without changing health or damage.
- **Defenders build their plan around fortified ground.** The AI groups nearby cover into positions, values protection and firing lanes above raw distance, and reserves distinct slots. After arriving, each defender takes one useful trench or building position—or holds the arrival point when none is free—and stays put unless that position is destroyed or unsafe.
- **Static weapons are a core part of every defence.** Defenders immediately attempt to crew every viable gun in the objective area while preserving leaders, key specialists, donor-squad strength, and a complete mobile reserve. Reported armour makes AP-capable guns the first staffing priority. Empty armed troop transports parked in the position, such as a halftrack with a mounted machine gun, are crewed the same way; tanks, assault guns, and aircraft are not.
- **AI-led transports dismount before disaster.** Infantry leave APCs when credible nearby contact or incoming fire makes remaining inside the greater risk, rather than waiting for the vehicle to be destroyed.

### Weapons, vehicles, and battlefield effects

- **AI has better fire discipline.** Handheld and mounted weapons check for friendlies in the firing lane. Grenades require sensible range, a clear target area, and a per-soldier cooldown; a stationary soldier can also throw an explosive grenade at a fresh last-known enemy position when the arcing path is clear.
- **Tanks behave more like armoured vehicles.** They hold useful fighting distances, reverse without exposing their rear, avoid pointless hull pivots, accelerate with more weight, and keep attacking when their orders call for pressure.
- **Infantry respond more sensibly to armour.** Riflemen hold their small-arms fire against a tank instead of plinking at it, anti-tank troops wait for a practical launcher shot rather than firing a rocket across the map, and crews on valid defensive gun assignments do not abandon their weapons because a separate suppression reaction tried to dismount them.
- **Thin cover is no longer automatically bulletproof.** Material-aware penetration measures cover thickness and resistance, carries reduced projectile energy through suitable props, and preserves entry, exit, tracer, decal, and ricochet feedback. Terrain, bunkers, vehicle armour, and native armour penetration remain meaningful.
- **Optional flight physics for your own aircraft.** Off by default. When enabled, planes you fly gain momentum, progressive stalls and spins, energy loss through hard manoeuvring, speed-dependent control authority, and damage-sensitive handling, with an optional compact instrument HUD. AI aircraft are never affected.
- **Soldiers flinch when they shoot.** The game's own recoil system was never fed for anyone but the soldier you control, so riflemen fired without their posture moving at all. Every other soldier now takes a weapon kick scaled from that weapon's recoil stat. Cosmetic by default; an optional switch also lets recoil disturb their aim.
- **Freelook no longer costs you the turn.** Holding the vehicle freelook button used to leave your aircraft with rudder only. The left stick now flies the plane for as long as you look — pitch and roll — while the right stick moves your head. Works in either stick layout and whether or not the flight model is enabled; rudder and throttle stand down on that stick only while you are actually manoeuvring.
- **Explosions have more presence without simply inflating every damage value.** Artillery missions, bomb effects, fragmentation, suppression, smoke, dust, craters, and small-explosion ragdoll force are reworked separately so each can be tuned on its own.

### Smaller fixes and presentation options

- More restrained AI command gestures, better animation quality for visible distant soldiers, and movement-aware ragdoll momentum.
- Allied multiplayer infantry can deliberately form one squad: open the player list, select a player, and choose **Join [player]'s squad**. Joining is per life and never happens automatically on spawn or respawn. Dead players can also leave their current squad and return to deployment.
- Configurable impact-decal lifetime, machine-gun tracer frequency, brightness, thickness, and independent streak length, battle chatter, simultaneous-sound capacity, distant sound shaping, weapon audio, tank engines, tracks, player footsteps, and rain/snow particle size, amount, and fall speed.
- Adjustable player suppression effects—including a larger near-miss radius and optional depth-of-field blur—plus recoverable aim fatigue, true 10x Caps Lock binocular zoom with the weapon model hidden, 200-degree hold-Alt freelook, corrected first-person body shadows, an immersive world HUD, and allied multiplayer names when other markers are hidden.
- A bullet to the head that kills you cuts to black and silence instantly instead of leaving the death camera on your body, with the death panel still readable over it; every other death is unchanged, and sight and sound always return when you take control of a soldier again.
- A map-north-aligned scrolling bottom-screen compass tape shown for five seconds with **K**, using NATO mils by default with optional degree bearings, tapered fading edges, and an option to keep it permanently visible.
- A built-in settings menu with Apply, Cancel, reset controls, individual switches for nearly every system, one-click Enable All / Disable All system kill switches, and Quality, Balanced, and Large Battle performance presets.

Press **F10**, or choose **Realism Overhaul Settings** from the main or pause menu. Quick Setup offers three performance starting points: **Quality** restores the recommended defaults, **Balanced** keeps every gameplay system while reducing the main animation/effects/audio costs, and **Large Battle** keeps the core AI and fire-safety systems while minimizing supplemental blast, fragmentation, ricochet, lingering-effect, decal, and audio work. Presets stage ordinary settings rather than hiding them, so every changed value remains individually editable before you press Apply. Audio-capacity changes take effect on the next launch.

Quick Setup and the AI / Core Behavior page also expose centered 0.5x-1.5x sliders for Aggressiveness, Accuracy, Reaction Speed, Awareness, and Suppression Resistance; 1.0x is the recommended baseline. Detailed AI options remain available under Objectives, Attack Bonuses, Defense, Infantry Tactics, Vehicle Tactics, Support, and Diagnostics. Defender static-weapon staffing has its own switch on the Defense page. Non-AI settings are unchanged.

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

Vehicle combat is the next major area being rebuilt. The first milestone is direct, non-screen-centering aiming for player-controlled ground-vehicle turrets and direct-fire stationary guns while preserving adjustable range zeroing. The next step is a read-only audit of Easy Red 2's existing whole-vehicle, engine, hull, fuel-tank, track, wheel, penetration, repair, destruction, and networked damage paths before changing any damage behavior. Large-calibre gun, shell, anti-tank weapon, and bomb ballistics will then be researched and reworked in bounded weapon families. Historically sourced vehicle armour values and any native damage tuning are conditional later phases, after the relevant game data and runtime behavior can be mapped reliably.

No replacement subsystem architecture or replacement of Easy Red 2's native whole-vehicle health model is planned. Initial inspection also indicates that infantry anti-tank weapons currently aim at a vehicle's general center rather than deliberately selecting a component; optional broad, externally visible aim zones will be considered only after the native damage audit and will not give AI hidden weak-point knowledge. The vehicle aiming, damage audit/tuning, zeroing, ballistics, and possible armour changes are still work in progress and are not part of v1.0.8; for now, the mod leaves Easy Red 2's existing vehicle aiming, damage, tank ballistics, and armour model alone.

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
