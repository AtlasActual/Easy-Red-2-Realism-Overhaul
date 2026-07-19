# Easy Red 2 Realism Overhaul

*A configurable overhaul for more believable combined-arms battles.*

ER2RealismOverhaul is built around the rough edges that become hard to ignore after enough hours in Easy Red 2: bayonets missing at arm's length, attacking squads stalling in the open, defenders wandering out of good positions, troops riding an APC into obvious danger, and AI firing through friendlies.

The goal is to fix those moments without replacing the game underneath them. This is not a health or damage multiplier mod; Easy Red 2's missions, armour system, and basic damage model remain intact.

> **Current release:** 1.0
>
> **Compatibility:** Tested with the Easy Red 2 build available on July 19, 2026

## What it fixes and improves

### Infantry combat

- **Melee attacks connect at believable distances.** The base hit check is longer and slightly wider for both players and AI, greatly reducing point-blank ghost swings. At the default setting, an ordinary strike reaches roughly 1.32 m and a bayonet roughly 1.68 m. Damage is unchanged.
- **AI no longer snaps onto every target or tracks enemies forever.** Soldiers need time to visually acquire a target, have a limited forward field of view, and lose firing permission when a remembered target is no longer reasonably known.
- **Cover is treated as a position, not a suggestion.** Exposed infantry look for threat-facing cover, favour trenches and lower stances, avoid piling multiple soldiers into the same spot, and hold good cover instead of constantly shuffling away from it.
- **Suppression changes behaviour.** Pinned soldiers get low, exposed troops may crawl toward safety, mounted gunners duck and stop firing, and heavily suppressed squads see, remember, and report less accurately.
- **Movement and weapon handling are less awkward.** Riflemen stop before firing, SMGs retain limited close-range moving fire, exposed AI reload from a safer posture, and crawling soldiers must stop before reloading or bandaging.
- **Squads can suppress a position without seeing through walls.** A stationary machine gunner may fire one short burst at a fresh, personally confirmed last-known position, using real ammunition and without tracking an invisible target.

### Offensive and defensive AI

- **Attacks are less likely to die after first contact.** Assaulting squads establish firing halts, use forward cover, and resume their advance instead of remaining stuck in an open-ended firefight. A modest configurable attacker bonus helps offensive AI maintain pressure without changing health or damage.
- **Defenders make better use of the ground they already hold.** Soldiers remain in useful protected positions, support elements cover likely approaches, and reserves are not thrown forward without a reason.
- **High Command gives battles a larger plan.** When enabled, it assigns assault, flank, support-by-fire, reserve, armour, aircraft, artillery, smoke, and anti-tank tasks around the main objective. It considers strength, suppression, terrain, congestion, and reported contacts before committing an attack.
- **AI-led transports dismount before disaster.** Infantry leave APCs when credible nearby contact or incoming fire makes remaining inside the greater risk, rather than waiting for the vehicle to be destroyed.
- **Battlefield information is imperfect.** Squads pass last-known positions by voice or radio with delays, confidence loss, and positional error instead of sharing a live, perfectly tracked enemy.

### Weapons, vehicles, and battlefield effects

- **AI has better fire discipline.** Handheld, mounted, and aircraft weapons check for friendlies in the firing lane. Grenades require sensible range, a clear target area, and a per-soldier cooldown.
- **Tanks behave more like armoured vehicles.** They hold useful fighting distances, reverse without exposing their rear, avoid pointless hull pivots, accelerate with more weight, and keep attacking when their orders call for pressure.
- **Infantry respond more sensibly to armour.** Exposed riflemen can seek tank-masked cover, anti-tank troops hold their ground, and threatened squads can crew a nearby empty anti-tank gun.
- **Thin cover is no longer automatically bulletproof.** Material-aware penetration measures cover thickness and resistance, carries reduced projectile energy through suitable props, and preserves entry, exit, tracer, decal, and ricochet feedback. Terrain, bunkers, vehicle armour, and native armour penetration remain meaningful.
- **Aircraft are safer and less weightless.** AI avoids friendly bomb impacts, reacts to nearby hostile fire, and can use configurable flight physics with momentum, stalls, energy loss, damage effects, and compact flight instruments.
- **Explosions have more presence without simply inflating every damage value.** Artillery missions, bomb effects, fragmentation, suppression, smoke, dust, craters, and small-explosion ragdoll force are reworked separately so each can be tuned on its own.

### Smaller fixes and presentation options

- More restrained AI command gestures and better animation quality for visible distant soldiers.
- Configurable impact-decal lifetime, tracer frequency, battle chatter, distant sound shaping, weapon audio, tank engines, tracks, and player footsteps.
- Adjustable player suppression effects, hold-breath zoom, first-person shadows, aircraft instruments, and allied multiplayer names when the rest of the HUD is hidden.
- A built-in settings menu with Apply, Cancel, reset controls, and individual switches for nearly every system.

Press **F10**, or choose **Realism Overhaul Settings** from the main or pause menu. The defaults are intended to work as a complete overhaul, but individual features can be reduced or disabled if you prefer a lighter touch.

## Coming soon

Tank combat is the next major area being rebuilt. Tank ballistics, armour values, and internal vehicle subsystems are all being reworked for a future release. These changes are still work in progress and are not part of v1.0; for now, the mod leaves Easy Red 2's existing tank ballistics and armour model alone.

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

If the original host leaves and an unmodded player becomes host, the overhaul can no longer run its host-authoritative systems. For the best results, a player with the same mod version should remain host.

## Compatibility and feedback

Easy Red 2 updates can change the game code that BepInEx mods rely on. If the mod stops loading after a game update, or something behaves differently online than it does offline, please report it on the project's [Issues page](../../issues).

## License

ER2RealismOverhaul is **source-available, not open source**. Personal, non-commercial use and private modification are permitted. Republishing, re-uploading, redistributing, selling, including the mod in a mod pack, or releasing modified copies is not permitted. See the [ER2RealismOverhaul Personal Use License](LICENSE) for the complete terms.
