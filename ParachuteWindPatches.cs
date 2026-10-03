using Corvostudio.Rendering;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace ER2RealismOverhaul;

internal static class ParachuteDrift
{
    // Keep extreme custom weather profiles from turning a parachute into an aircraft.
    private const float WindDriftFraction = 0.4f;
    private const float MaximumWindDriftSpeed = 5f;
    private const float AircraftMomentumFraction = 0.1f;
    private const float MaximumAircraftDriftSpeed = 7f;
    private const float AircraftDriftDuration = 2.5f;
    private const float PendingMomentumLifetime = 60f;

    private static readonly Dictionary<int, PendingMomentum> PendingMomentumBySoldier = new();
    private static readonly Dictionary<int, ActiveMomentum> ActiveMomentumByParachute = new();
    private static readonly Dictionary<int, CanopyState> Canopies = new();
    private static float _nextPendingCleanup;
    private static bool _loggedFailure;

    private readonly record struct PendingMomentum(Vector3 Velocity, float CapturedAt);
    private readonly record struct ActiveMomentum(Vector3 Velocity, float StartedAt);
    private readonly record struct CanopyState(ParachuteProfile Profile, float StartedAt);
    internal readonly record struct DescentStep(bool Active, float Height, float Scale);

    internal static void CaptureAircraftExit(Soldier soldier, Seats seat, Vehicle exitingFrom)
    {
        if (soldier == null || seat != null || exitingFrom == null ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        if (!Il2CppTypeTests.Is<VehiclePlane>(exitingFrom) &&
            !Il2CppTypeTests.Is<CargoPlane>(exitingFrom) && !Il2CppTypeTests.Is<Glider>(exitingFrom))
            return;

        try
        {
            if (Time.time >= _nextPendingCleanup)
            {
                _nextPendingCleanup = Time.time + 10f;
                foreach (var entry in PendingMomentumBySoldier.ToArray())
                    if (Time.time - entry.Value.CapturedAt > PendingMomentumLifetime)
                        PendingMomentumBySoldier.Remove(entry.Key);
            }
            var rigidbody = exitingFrom.GetRigidbody();
            var velocity = rigidbody != null
                ? rigidbody.velocity
                : exitingFrom.transform.forward * exitingFrom.GetCurrentSpeed();
            velocity.y = 0f;

            if (!IsFinite(velocity) || velocity.sqrMagnitude < 0.01f)
                return;

            velocity = Vector3.ClampMagnitude(
                velocity * AircraftMomentumFraction,
                MaximumAircraftDriftSpeed);
            PendingMomentumBySoldier[soldier.GetInstanceID()] =
                new PendingMomentum(velocity, Time.time);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
    }

    internal static void AttachAircraftMomentum(Vehicle vehicle, Soldier soldier)
    {
        var parachute = Il2CppTypeTests.As<VehicleParachute>(vehicle);
        if (parachute == null || soldier == null ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return;
        }

        try
        {
            var soldierId = soldier.GetInstanceID();
            if (!PendingMomentumBySoldier.Remove(soldierId, out var pending))
                return;

            if (Time.time - pending.CapturedAt > PendingMomentumLifetime)
                return;

            ActiveMomentumByParachute[parachute.GetInstanceID()] =
                new ActiveMomentum(pending.Velocity, Time.time);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
    }

    internal static DescentStep Apply(VehicleParachute parachute)
    {
        if (parachute == null || !MultiplayerAuthority.CanMutateGameplay() ||
            parachute.phase != 0 || !parachute.isMoving)
            return default;

        try
        {
            int id = parachute.GetInstanceID();
            if (!Canopies.TryGetValue(id, out var canopy))
            {
                canopy = new CanopyState(ParachuteDriftCore.CreateProfile(id), Time.time);
                Canopies[id] = canopy;
            }
            var position = parachute.transform.position;
            float age = Time.time - canopy.StartedAt;
            var wind = GetWindVelocity();
            var drift = ParachuteDriftCore.HorizontalVelocity(canopy.Profile,
                new System.Numerics.Vector2(wind.x, wind.z),
                new System.Numerics.Vector2(position.x, position.z), Time.time, age);
            var displacement = GetAircraftMomentumDisplacement(parachute) +
                new Vector3(drift.X, 0f, drift.Y) * Time.fixedDeltaTime;
            if (displacement.sqrMagnitude > 0.000001f)
                parachute.transform.Translate(displacement, Space.World);
            return new DescentStep(true, position.y,
                ParachuteDriftCore.DescentScale(canopy.Profile, age));
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return default;
        }
    }

    internal static void FinishDescent(VehicleParachute parachute, DescentStep step)
    {
        if (!step.Active || parachute == null)
            return;
        try
        {
            // Native MoveDown owns terrain/building contact and sets phase to 1
            // on landing. Never alter its landing snap or ejection sequence.
            if (parachute.phase != 0)
            {
                Remove(parachute);
                return;
            }
            var position = parachute.transform.position;
            float descent = step.Height - position.y;
            if (float.IsFinite(descent) && descent > 0f)
                parachute.transform.Translate(Vector3.down * (descent * (step.Scale - 1f)), Space.World);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }
    }

    internal static void Remove(Vehicle vehicle)
    {
        var parachute = Il2CppTypeTests.As<VehicleParachute>(vehicle);
        if (parachute != null)
        {
            ActiveMomentumByParachute.Remove(parachute.GetInstanceID());
            Canopies.Remove(parachute.GetInstanceID());
        }
    }

    private static Vector3 GetAircraftMomentumDisplacement(VehicleParachute parachute)
    {
        var parachuteId = parachute.GetInstanceID();
        if (!ActiveMomentumByParachute.TryGetValue(parachuteId, out var momentum))
            return Vector3.zero;

        var strength = 1f - ((Time.time - momentum.StartedAt) / AircraftDriftDuration);
        if (strength <= 0f)
        {
            ActiveMomentumByParachute.Remove(parachuteId);
            return Vector3.zero;
        }

        return momentum.Velocity * (Mathf.Clamp01(strength) * Time.fixedDeltaTime);
    }

    private static Vector3 GetWindVelocity()
    {
        var stack = VolumeManager.instance?.stack;
        var weather = stack?.GetComponent<ER2VolumetricCloudsVolume>();
        if (weather?.windDirection == null || weather.windSpeed == null)
            return Vector3.zero;

        var direction = weather.windDirection.value;
        if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) ||
            !float.IsFinite(weather.windSpeed.value))
            return Vector3.zero;
        var speed = Mathf.Clamp(
            weather.windSpeed.value * WindDriftFraction,
            0f,
            MaximumWindDriftSpeed);
        if (direction.sqrMagnitude < 0.0001f || speed <= 0.01f)
            return Vector3.zero;

        direction.Normalize();
        return new Vector3(direction.x, 0f, direction.y) * speed;
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    private static void LogFailure(Exception ex)
    {
        if (_loggedFailure)
            return;

        _loggedFailure = true;
        Plugin.LogSource.LogWarning(
            $"Parachute drift adjustment is unavailable; using native descent: {ex.Message}");
    }
}

[HarmonyPatch(typeof(Soldier), "SetOnVehicle", new[] { typeof(Seats), typeof(Vehicle) })]
internal static class ParachuteAircraftExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(Soldier __instance, Seats seat, Vehicle exitingFrom)
    {
        ParachuteDrift.CaptureAircraftExit(__instance, seat, exitingFrom);
    }
}

[HarmonyPatch(typeof(Vehicle), "GetOnVehicle", new[] { typeof(int), typeof(Soldier) })]
internal static class ParachuteAircraftMomentumAttachPatch
{
    [HarmonyPostfix]
    private static void Postfix(Vehicle __instance, Soldier soldier)
    {
        ParachuteDrift.AttachAircraftMomentum(__instance, soldier);
    }
}

[HarmonyPatch(typeof(VehicleParachute), "MoveDown")]
internal static class ParachuteMoveDownPatch
{
    [HarmonyPrefix]
    private static void Prefix(VehicleParachute __instance, out ParachuteDrift.DescentStep __state)
    {
        __state = ParachuteDrift.Apply(__instance);
    }

    [HarmonyPostfix]
    private static void Postfix(VehicleParachute __instance, ParachuteDrift.DescentStep __state)
        => ParachuteDrift.FinishDescent(__instance, __state);
}

[HarmonyPatch(typeof(Vehicle), "OnDestroy")]
internal static class ParachuteDestroyPatch
{
    [HarmonyPrefix]
    private static void Prefix(Vehicle __instance)
    {
        ParachuteDrift.Remove(__instance);
    }
}
