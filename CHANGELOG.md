# Changelog

This file records only concrete, player-visible changes in each released version of Easy Red 2 Realism Overhaul. It is a living description of the shipped mod: superseded wording is replaced with the final behavior, and changes that are removed or reverted are deleted instead of retained as historical notes.

## 1.0.2 - 2026-07-20

Easy Red 2 compatibility: Steam public branch, build `24246380`.

- Normal melee and bayonet attacks now consistently use the configured longer, wider hit area on the actual damage frame, instead of intermittently falling back to Easy Red 2's much shorter native range.
- Fixed the scrolling compass losing the map's true-north offset when the tactical map was closed or starting uncalibrated when it had never been opened. The compass now initializes from the current map's persistent north-direction setting, while the tactical map refreshes that reference whenever available.
- High Command now uses one host-authoritative ground director to derive attack or defence from objective ownership and arbitrate squad, soldier, vehicle, aircraft, emplacement, and support orders. Player and mission-script orders take priority, while stable command leases prevent AI systems from repeatedly overwriting one another.
- Defender static-weapon staffing now continuously fills every viable objective-area gun that available crews can reach, prioritizes AP-capable weapons when armour is reported, preserves leaders, key specialists, minimum squad strength, and a complete mobile reserve, and restores a protected gunner route if native AI clears it.
- Attacking infantry now use protected positions as deliberate bounds and support-by-fire halts, stop to fight on contact, and resume only when the tactical attack gate authorizes movement.
- Fixed controller-owned soldiers being mistaken for AI when the first-person camera check was unavailable, which could block weapon fire and interfere with other player actions.
- Fixed aircraft becoming unresponsive with **Simplified** controls by preserving Easy Red 2's native simplified flight controller; **Realistic Mouse**, **Realistic Keyboard**, and AI aircraft continue using the overhaul flight model.
- Player-issued "Get In" orders now supersede automatic defender staffing on static weapons, and the first available squad member prioritizes the gunner seat so a partial AT-gun crew remains usable.
- Defenders now make one deliberate move from exposed arrival points into genuinely protective trenches, buildings, or other fortified cover, prioritizing protection over an immediate firing lane and reserving enough physical space to prevent soldiers from stacking in the same position. Reached building and trench slots remain latched even when native cover-state reporting flickers or clears its destination.
- Defenders who reach useful cover now hold that position and let attackers come to them instead of circulating out into the open under fire. They relocate only when the defensive order changes or the position is destroyed, unsafe, or materially degraded.
- Unified cover and suppression posture control prevents AI from repeatedly switching between prone and crouched, while still allowing them to rise when a genuinely protective position has a usable firing line.
- Infantry cover scoring now measures material resistance, thickness, and protection across the whole body. Foliage, glass, canvas, and thin props no longer count like earthworks, sandbags, masonry, or substantial building cover.
- Simplified contact-response tuning by consolidating low-level cover timers, candidate limits, reservation spacing, and scoring into one stable policy; cover search radius remains adjustable.
- AI infantry now stop and briefly hold when a movement destination produces no real progress, preventing walking-in-place loops during cover movement, commander routes, and static-weapon transit. Repeated failures at the same destination produce progressively longer holds instead of constant retries.
- Reduced combat stutter by bounding expensive cover evaluation, removing allocation-heavy physics queries from hot paths, and throttling repeated commander, contact-sharing, and firing-lane work.
- Reworked the local-player suppression blur so its intensity scales linearly with the suppression actually received, rapid hits stack, and the effect fully clears after incoming suppression stops.
- Fixed `VignetteMultiplier` so it reliably scales the native suppression vignette and applies setting changes immediately.
- Fixed repeated gun-audio null-reference errors by allowing distant-sound reflections and Easy Red 2's native weapon reverb to share one filter safely.
- Player suppression now supports a configurable expanded near-miss radius, allowing bullets outside Easy Red 2's native flyby radius to contribute suppression without double-counting native hits.
- Nearby allied wounds and deaths now cause a configurable morale shock for autonomous AI, with deaths producing the stronger and wider suppression effect.
- Allied multiplayer infantry can deliberately form one squad by selecting another allied player and choosing **Join [player]'s squad**. Joining moves only the current soldier, excludes vehicle crews, and must be chosen again after each respawn.
- AI with low-velocity anti-tank launchers keep their primary weapons equipped and withhold launcher fire beyond a configurable 90 m default (40–160 m); they switch to the launcher only after the target enters range, while high-velocity anti-tank rifles retain their normal range.
- AI aircraft now autonomously engage visible enemy aircraft and strafe visible ground targets of opportunity while they have no High Command assignment; commander missions, scripted flight states, current attacks, and evasive maneuvers retain priority.
- Commander aircraft ground strikes now remain near the active objective, and AI bombs require a valid impact prediction plus a live hostile ground target within 65 m, preventing releases over empty terrain or after a target disappears.
- AI tanks now clear stale steering when stopped, turn toward route nodes in a forward arc instead of spinning in place, and avoid pivoting their hull away from an armoured threat while the turret can continue tracking it.
- Added first-person binoculars toggled with **Caps Lock**, with a clean unmarked overlay, true configurable optical magnification that defaults to 10×, and automatic first-person weapon-model hiding while active.
- Added hold-to-freelook on either **Alt** key while alive and on foot. The horizontal arc defaults to 200° (100° to either side), is configurable, and smoothly recenters on release without turning the soldier or weapon.
- Added a map-north-aligned scrolling bottom-screen compass tape with tapered, fading edges. Pressing **K** shows it for five seconds; it defaults to NATO angular mils (0–6400), can use degree bearings instead, and has an option to remain permanently visible during gameplay.
- Reorganized AI controls into Commander, Infantry Tactics, Vehicle Tactics, Support Coordination, Attack Posture Bonuses, and Diagnostics pages. Existing values are migrated from the former sections, and static-weapon staffing is now part of Commander doctrine rather than an independent toggle.
