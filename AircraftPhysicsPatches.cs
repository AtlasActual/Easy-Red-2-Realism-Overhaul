using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

internal readonly record struct AircraftFlightProfile(
    string Name,
    float CriticalAngleOfAttack,
    float MaximumPitchRate,
    float MaximumRollRate,
    float MaximumYawRate,
    float ParasiteDrag,
    float InducedDrag,
    float SideslipDamping,
    float StallDrag,
    float StallSinkGravity,
    float AngularDamping,
    float MissingWingRollAcceleration,
    float LandingGearDrag,
    float StallPitchRate,
    float SpinYawRate,
    float SpinRollRate,
    float BestGlideLiftToDrag);

internal static class AircraftFlightProfiles
{
    private static readonly AircraftFlightProfile Fighter = new(
        "fighter", 17f, 46f, 88f, 27f,
        0.12f, 1.55f, 0.085f, 3.8f, 0.26f, 0.18f, 1.35f, 0.85f,
        38f, 40f, 80f, 12f);

    private static readonly AircraftFlightProfile Bomber = new(
        "bomber", 15f, 27f, 42f, 16f,
        0.17f, 1.95f, 0.11f, 4.4f, 0.30f, 0.26f, 0.72f, 1.15f,
        24f, 24f, 44f, 14f);

    internal static AircraftFlightProfile For(VehiclePlane plane)
        => plane.planeType == PlaneType.Bomber ? Bomber : Fighter;
}

internal static class AircraftTuning
{
    private static bool UseAdvancedConfig => Settings.AircraftAdvancedTuningEnabled.Value;

    internal static float ControlResponse => UseAdvancedConfig
        ? Settings.AircraftControlResponseMultiplier.Value : 0.78f;
    internal static float EngineResponse => UseAdvancedConfig
        ? Settings.AircraftEngineResponseMultiplier.Value : 1.25f;
    internal static float ThrottleReductionResponse => UseAdvancedConfig
        ? Settings.AircraftThrottleReductionResponseMultiplier.Value : 1.8f;
    internal static float EnergyLoss => UseAdvancedConfig
        ? Settings.AircraftEnergyLossMultiplier.Value : 1f;
    internal static float NativeCoastDrag => UseAdvancedConfig
        ? Settings.AircraftNativeCoastDragMultiplier.Value : 1f;
    internal static float NativeVelocityLoss => UseAdvancedConfig
        ? Settings.AircraftNativeVelocityLossMultiplier.Value : 0f;
    internal static float GlideEnergyLoss => UseAdvancedConfig
        ? Settings.AircraftGlideEnergyLossMultiplier.Value : 1f;
    internal static float MaximumEnergyRetentionAcceleration => UseAdvancedConfig
        ? Settings.AircraftMaximumEnergyRetentionAcceleration.Value : 6f;
    internal static float StallRecoveryPitchAuthority => UseAdvancedConfig
        ? Settings.AircraftStallRecoveryPitchAuthority.Value : 0.58f;
    internal static float StallRecoveryRollAuthority => UseAdvancedConfig
        ? Settings.AircraftStallRecoveryRollAuthority.Value : 0.72f;
    internal static float StallNoseDropStrength => UseAdvancedConfig
        ? Settings.AircraftStallNoseDropStrength.Value : 1f;
    internal static float SpinStrength => UseAdvancedConfig
        ? Settings.AircraftSpinStrength.Value : 1f;
    internal static float SpinRecoverySpeed => UseAdvancedConfig
        ? Settings.AircraftSpinRecoverySpeedMultiplier.Value : 1.22f;
}

internal sealed class AircraftFlightState
{
    internal AircraftFlightProfile Profile;
    internal float OriginalClocheMultiplier;
    internal float OriginalThrustResponseSeconds;
    internal float OriginalThrustForceMultiplier;
    internal float OriginalMaximumSpeedKmh;
    internal float OriginalStartLiftMultiplier;
    internal float OriginalEndLiftMultiplier;
    internal float BaseStallSpeedMs;
    internal float BaseReferenceSpeedMs;
    internal float BaseMaximumSpeedMs;
    internal float StallSpeedMs;
    internal float ReferenceSpeedMs;
    internal float MaximumSpeedMs;
    internal float AirspeedMs;
    internal float ForwardSpeedMs;
    internal float VerticalSpeedMs;
    internal float AngleOfAttack;
    internal float SideslipAngle;
    internal float ControlAuthority = 1f;
    internal float PitchAuthority = 1f;
    internal float RollAuthority = 1f;
    internal float YawAuthority = 1f;
    internal float StallSeverity;
    internal bool IsStalled;
    internal float DeepStallTime;
    internal float SpinRecoveryTime;
    internal float SpinSeverity;
    internal float SpinDirection;
    internal bool IsSpinning;
    internal float StallPitchBlend;
    internal float StallPitchError;
    internal bool HasEnergySample;
    internal float PreviousSpecificEnergy;
    internal Vector3 PreviousEnergyPosition;
    internal float MeasuredEnergyLossRate;
    internal float EnergyRetentionAcceleration;
    internal float ModeledDragAcceleration;
    internal float NativeVelocityCorrectionMs;
    internal float AvailableEngineAccelerationMs2;
    internal float NextGearTrace;
    internal bool LeftWingLost;
    internal bool RightWingLost;
    internal float TailLoss;
    internal float PropellerLoss;
    internal float TerrainClearance = -1f;
    internal float NextTerrainCheck;
    internal float NextTelemetry;
    internal bool LoggedFailure;
}

internal static class AircraftFlightPhysics
{
    private const float NativeMaximumThrottle = 100f;
    private static readonly Dictionary<int, AircraftFlightState> States = new();

    [ThreadStatic]
    private static VehiclePlane? activeNativeFixedUpdatePlane;

    internal static void Initialize(VehiclePlane plane)
    {
        if (plane == null || !ShouldApply(plane))
            return;

        var id = plane.GetInstanceID();
        if (States.ContainsKey(id))
            return;

        try
        {
            var profile = AircraftFlightProfiles.For(plane);
            var maximumSpeed = ValidPositive(plane.maxKmhSpeed)
                ? Mathf.Clamp(plane.maxKmhSpeed / 3.6f, 45f, 240f)
                : profile.Name == "bomber" ? 120f : 165f;

            var nativeFullLiftSpeed = plane.totalLiftVelocity;
            var stallSpeed = ValidPositive(nativeFullLiftSpeed) && nativeFullLiftSpeed < maximumSpeed * 0.72f
                ? nativeFullLiftSpeed * 0.88f
                : maximumSpeed * (profile.Name == "bomber" ? 0.30f : 0.24f);
            stallSpeed = Mathf.Clamp(stallSpeed, 18f, maximumSpeed * 0.55f);
            var referenceSpeed = Mathf.Max(stallSpeed * 1.85f, maximumSpeed * 0.60f);

            States[id] = new AircraftFlightState
            {
                Profile = profile,
                OriginalClocheMultiplier = plane.clocheMultiplier,
                OriginalThrustResponseSeconds = plane.timeFromZeroToMaxThrust,
                OriginalThrustForceMultiplier = plane.thrustForceMultiplier,
                OriginalMaximumSpeedKmh = plane.maxKmhSpeed,
                OriginalStartLiftMultiplier = plane.startLiftMult,
                OriginalEndLiftMultiplier = plane.endLiftMult,
                BaseStallSpeedMs = stallSpeed,
                BaseReferenceSpeedMs = referenceSpeed,
                BaseMaximumSpeedMs = maximumSpeed,
                StallSpeedMs = stallSpeed,
                ReferenceSpeedMs = referenceSpeed,
                MaximumSpeedMs = maximumSpeed,
                NextTelemetry = Time.unscaledTime + UnityEngine.Random.Range(0f, 0.5f)
            };

            AiState.Trace(
                $"Aircraft physics: initialized {SafeName(plane)} as {profile.Name}, " +
                $"stall={stallSpeed * 3.6f:0}km/h reference={Mathf.Max(stallSpeed * 1.85f, maximumSpeed * 0.60f) * 3.6f:0}km/h " +
                $"maximum={maximumSpeed * 3.6f:0}km/h");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning($"Aircraft physics initialization failed: {ex.Message}");
        }
    }

    internal static float BeginNativeFixedUpdate(VehiclePlane plane)
    {
        activeNativeFixedUpdatePlane = null;
        var originalFallDrag = VehiclePlane.PLANE_FALL_DRAG;
        try
        {
            if (plane != null && ShouldApply(plane) &&
                Settings.AircraftEnergyRetentionEnabled.Value)
            {
                var blend = Mathf.Clamp01(Settings.AircraftPhysicsStrength.Value);
                VehiclePlane.PLANE_FALL_DRAG = originalFallDrag * Mathf.Lerp(
                    1f, AircraftTuning.NativeCoastDrag, blend);
                activeNativeFixedUpdatePlane = plane;
                if (States.TryGetValue(plane.GetInstanceID(), out var state))
                    state.NativeVelocityCorrectionMs = 0f;
            }
        }
        catch
        {
            VehiclePlane.PLANE_FALL_DRAG = originalFallDrag;
        }

        return originalFallDrag;
    }

    internal static void EndNativeFixedUpdate(float originalFallDrag)
    {
        activeNativeFixedUpdatePlane = null;
        VehiclePlane.PLANE_FALL_DRAG = originalFallDrag;
    }

    internal static void ReplaceNativeVelocityLoss(Rigidbody rigidbody, ref Vector3 requestedVelocity)
    {
        var plane = activeNativeFixedUpdatePlane;
        if (plane == null || rigidbody == null ||
            !Settings.AircraftEnergyRetentionEnabled.Value)
        {
            return;
        }

        try
        {
            var planeRigidbody = plane.GetRigidbody();
            if (planeRigidbody == null ||
                planeRigidbody.GetInstanceID() != rigidbody.GetInstanceID() ||
                plane.isGrounded || !plane.IsInAerodynamicMode())
            {
                return;
            }

            var currentVelocity = rigidbody.velocity;
            var currentSpeed = currentVelocity.magnitude;
            var requestedSpeed = requestedVelocity.magnitude;
            if (currentSpeed < 1.5f || requestedSpeed < 0.1f ||
                requestedSpeed >= currentSpeed - 0.001f)
            {
                return;
            }

            // VehiclePlane rotates the velocity toward the nose, then removes a fixed
            // percentage of its complete magnitude before calling SetVelocity. Limit
            // interception to that small, direction-preserving write so collisions and
            // network corrections remain authoritative.
            var lostFraction = (currentSpeed - requestedSpeed) / currentSpeed;
            var directionAlignment = Vector3.Dot(
                currentVelocity / currentSpeed, requestedVelocity / requestedSpeed);
            if (lostFraction > 0.20f || directionAlignment < 0.85f)
                return;

            var strength = Mathf.Clamp01(Settings.AircraftPhysicsStrength.Value);
            var retainedNativeFraction = Mathf.Lerp(
                1f, AircraftTuning.NativeVelocityLoss, strength);
            var correctedSpeed = Mathf.Lerp(currentSpeed, requestedSpeed, retainedNativeFraction);
            requestedVelocity = requestedVelocity / requestedSpeed * correctedSpeed;

            if (States.TryGetValue(plane.GetInstanceID(), out var state))
                state.NativeVelocityCorrectionMs = correctedSpeed - requestedSpeed;
        }
        catch
        {
            // A despawning rigidbody can invalidate an interop wrapper mid-call. Leave
            // the requested native velocity untouched in that case.
        }
    }

    internal static void DecoupleThrottleSpeedGovernor(
        VehiclePlane plane, ref bool canApplyThrust)
    {
        if (canApplyThrust || plane == null ||
            !Settings.AircraftThrottleControlsEnginePower.Value ||
            !Settings.AircraftFlightPhysicsEnabled.Value ||
            !Settings.AircraftEnergyRetentionEnabled.Value)
        {
            return;
        }

        try
        {
            if (!ShouldApply(plane) ||
                plane.hasMissingParts || !plane.engineStarted ||
                plane.isGrounded || !plane.IsInAerodynamicMode())
            {
                return;
            }

            var normalizedThrottle = NormalizedThrottle(plane);
            if (normalizedThrottle <= 0.001f)
                return;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null)
                return;

            var forwardSpeed = plane.transform.InverseTransformDirection(rigidbody.velocity).z;
            var nativeThrottleSpeed = Mathf.Max(0f, plane.maxKmhSpeed / 3.6f) *
                                      normalizedThrottle;
            // A false argument at or above this threshold is the native speed governor.
            // Other false paths (damage, engine state, authority) remain untouched.
            if (forwardSpeed >= nativeThrottleSpeed - 0.5f)
                canApplyThrust = true;
        }
        catch
        {
            // Preserve the native decision if state changes during the call.
        }
    }

    internal static void SmoothNativeThrottleReduction(
        VehiclePlane plane, bool canApplyThrust, float previousThrust)
    {
        if (plane == null || !canApplyThrust ||
            !Settings.AircraftEnergyRetentionEnabled.Value ||
            AircraftTuning.ThrottleReductionResponse <= 1f ||
            !TryGetState(plane, out var state))
        {
            return;
        }

        try
        {
            // Never preserve power through a failed propeller, developed stall, spin,
            // or overspeed condition. A false canApplyThrust also bypasses this patch,
            // so native engine shutdown and damage behavior remains authoritative.
            if (state.PropellerLoss > 0.02f || state.StallSeverity > 0.20f ||
                state.IsSpinning || state.ForwardSpeedMs < state.StallSpeedMs * 1.05f ||
                state.AirspeedMs > state.MaximumSpeedMs * 1.05f)
            {
                return;
            }

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || !IsStraightEnergyFlight(plane, rigidbody, state))
                return;

            var nativeThrust = plane.thrustForce;
            var targetThrust = Mathf.Max(0f, plane._trgtThrust);
            if (previousThrust <= targetThrust || nativeThrust >= previousThrust)
                return;

            // The native controller falls toward the target at twice its rise rate.
            // Reconstruct the rise-rate step, then lengthen only the downward response.
            var responseSeconds = Mathf.Max(0.05f, plane.timeFromZeroToMaxThrust);
            var downwardStep = Time.fixedDeltaTime * (60f / responseSeconds) * 1000f /
                               AircraftTuning.ThrottleReductionResponse;
            var smoothedThrust = Mathf.MoveTowards(
                previousThrust, targetThrust, downwardStep);
            plane.thrustForce = Mathf.Max(nativeThrust, smoothedThrust);
        }
        catch
        {
            // Interop values can become invalid while a vehicle is despawning. In that
            // case leave the native thrust result untouched.
        }
    }

    internal static void FixedUpdate(VehiclePlane plane)
    {
        if (plane == null)
            return;

        var id = plane.GetInstanceID();
        if (!ShouldApply(plane))
        {
            if (States.TryGetValue(id, out var inactive))
            {
                RestoreNativeValues(plane, inactive);
                States.Remove(id);
                TryBeginAbandonedCoast(plane, id);
            }

            MaintainAbandonedMomentum(plane, id);
            return;
        }

        Abandoned.Remove(id);

        if (!States.TryGetValue(id, out var state))
        {
            Initialize(plane);
            if (!States.TryGetValue(id, out state))
                return;
        }

        try
        {
            UpdateDamageState(plane, state);
            ApplyNativeTuning(plane, state);

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || rigidbody.isKinematic)
            {
                ResetSpinAndEnergyState(state);
                return;
            }

            var velocity = rigidbody.velocity;
            var speed = velocity.magnitude;
            var localVelocity = plane.transform.InverseTransformDirection(velocity);
            state.AirspeedMs = speed;
            state.ForwardSpeedMs = localVelocity.z;
            state.VerticalSpeedMs = velocity.y;

            if (plane.isGrounded || !plane.IsInAerodynamicMode())
            {
                state.AngleOfAttack = 0f;
                state.SideslipAngle = 0f;
                state.StallSeverity = Mathf.MoveTowards(state.StallSeverity, 0f, Time.fixedDeltaTime * 2f);
                state.IsStalled = false;
                ResetSpinAndEnergyState(state);
                state.ControlAuthority = 1f;
                state.PitchAuthority = 1f;
                state.RollAuthority = 1f;
                state.YawAuthority = 1f;
                WriteTelemetry(plane, state);
                return;
            }

            state.AngleOfAttack = Mathf.Atan2(-localVelocity.y, localVelocity.z) * Mathf.Rad2Deg;
            state.SideslipAngle = Mathf.Atan2(localVelocity.x, localVelocity.z) * Mathf.Rad2Deg;

            UpdateStallAndAuthority(plane, state);
            UpdateSpinState(plane, rigidbody, state);
            ApplyEnergyRetention(plane, rigidbody, state, velocity);
            ApplyAerodynamicCorrections(plane, rigidbody, state, velocity, localVelocity);
            LimitAngularVelocity(plane, rigidbody, state);
            WriteTelemetry(plane, state);
        }
        catch (Exception ex)
        {
            if (state.LoggedFailure)
                return;

            state.LoggedFailure = true;
            Plugin.LogSource.LogWarning(
                $"Aircraft physics disabled for {SafeName(plane)} after an update failure: {ex.Message}");
            RestoreNativeValues(plane, state);
            States.Remove(id);
        }
    }

    internal static bool TryGetState(VehiclePlane plane, out AircraftFlightState state)
    {
        state = null!;
        if (plane == null || !ShouldApply(plane))
            return false;

        var id = plane.GetInstanceID();
        if (States.TryGetValue(id, out var existing))
        {
            state = existing;
            return true;
        }

        Initialize(plane);
        if (States.TryGetValue(id, out existing))
        {
            state = existing;
            return true;
        }

        return false;
    }

    internal static bool AllowLandingGearExtension(VehiclePlane plane)
    {
        if (plane == null)
            return true;

        try
        {
            // Gear must remain usable while parked, taking off, or after touchdown.
            // The native low-altitude automation otherwise calls OpenGear regardless
            // of airspeed, including during a fast terrain-following pass.
            if (plane.isGrounded)
                return true;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null)
                return true;

            var airspeed = rigidbody.velocity.magnitude;
            if (!float.IsFinite(airspeed))
                return true;

            AircraftFlightState? state = null;
            var profile = AircraftFlightProfiles.For(plane);
            var maximumSpeed = ValidPositive(plane.maxKmhSpeed)
                ? Mathf.Clamp(plane.maxKmhSpeed / 3.6f, 45f, 240f)
                : profile.Name == "bomber" ? 120f : 165f;
            var stallSpeed = maximumSpeed * (profile.Name == "bomber" ? 0.30f : 0.24f);
            if (TryGetState(plane, out var flightState))
            {
                state = flightState;
                maximumSpeed = flightState.MaximumSpeedMs;
                stallSpeed = flightState.StallSpeedMs;
            }

            // WWII gear-extension limits were well above approach speed but far below
            // combat speed. Deriving the limit from each aircraft's stall and maximum
            // speeds keeps the behavior consistent across fighters and bombers.
            var stallMultiplier = profile.Name == "bomber" ? 1.55f : 1.70f;
            var extensionLimit = Mathf.Min(
                maximumSpeed * 0.62f, stallSpeed * stallMultiplier);
            extensionLimit = Mathf.Clamp(
                extensionLimit, stallSpeed * 1.35f, maximumSpeed * 0.70f);
            if (airspeed <= extensionLimit)
                return true;

            if (state != null && Time.unscaledTime >= state.NextGearTrace)
            {
                state.NextGearTrace = Time.unscaledTime + 2f;
                AiState.Trace(
                    $"Landing gear interlock: {SafeName(plane)} blocked at " +
                    $"{airspeed * 3.6f:0}km/h (limit {extensionLimit * 3.6f:0}km/h)");
            }

            return false;
        }
        catch
        {
            // If an aircraft despawns during the check, preserve the native call.
            return true;
        }
    }

    internal static void ScalePlayerInputs(
        VehiclePlane plane, ref float yaw, ref float pitch, ref float roll)
    {
        if (!TryGetState(plane, out var state) || plane.isGrounded)
            return;

        var strength = Settings.AircraftPhysicsStrength.Value;
        var blend = Mathf.Clamp01(strength);
        yaw *= Mathf.Lerp(1f, state.YawAuthority, blend) *
               Mathf.Lerp(1f, 1f - state.TailLoss * 0.65f, blend);
        pitch *= Mathf.Lerp(1f, state.PitchAuthority, blend) *
                 Mathf.Lerp(1f, 1f - state.TailLoss * 0.55f, blend);
        roll *= Mathf.Lerp(1f, state.RollAuthority, blend) * Mathf.Lerp(
            1f, 1f - (BoolFloat(state.LeftWingLost) + BoolFloat(state.RightWingLost)) * 0.28f, blend);
    }

    internal static float TerrainClearanceAhead(VehiclePlane plane, AircraftFlightState state)
    {
        var now = Time.unscaledTime;
        if (now < state.NextTerrainCheck)
            return state.TerrainClearance;

        state.NextTerrainCheck = now + 0.25f;
        var flatForward = Vector3.ProjectOnPlane(plane.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.01f)
            flatForward = Vector3.forward;
        else
            flatForward.Normalize();

        var origin = plane.transform.position + flatForward * 30f + Vector3.up * 4f;
        state.TerrainClearance = Physics.Raycast(
            origin, Vector3.down, out var hit, 1000f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0f, hit.distance - 4f)
            : -1f;
        return state.TerrainClearance;
    }

    internal static void Remove(VehiclePlane plane)
    {
        if (plane == null)
            return;

        var id = plane.GetInstanceID();
        // The plane is going away, so any coast it was on goes with it.
        Abandoned.Remove(id);
        if (!States.TryGetValue(id, out var state))
            return;

        RestoreNativeValues(plane, state);
        States.Remove(id);
    }

    // Fraction of full rudder authority the propeller slipstream sustains on its own at
    // full power, independent of how fast the aircraft is travelling. The fin and rudder
    // of a single-engine fighter sit directly in the propwash, so they keep biting when
    // the wing has nearly stopped flying — that is what holds a taildragger straight on
    // the takeoff roll and what makes a hammerhead possible with the airspeed reading
    // near zero. Scaling the rudder off freestream speed alone gets this backwards.
    private const float RudderSlipstreamAuthority = 0.85f;

    // Where rudder force starts climbing out of reach, as a fraction of top speed, and
    // what authority is left at maximum speed. WWII fighters had unboosted controls: past
    // roughly cruise the pedal force for meaningful deflection rises with dynamic pressure
    // until the pilot simply cannot hold it, so the rudder goes heavy and vague exactly
    // where the wing still has plenty of authority. This is the real high-speed limit and
    // it is the opposite end of the envelope from the wing's low-speed one.
    private const float RudderHeavyOnsetSpeedFraction = 0.55f;
    private const float RudderAuthorityAtMaximumSpeed = 0.38f;

    // Where the rudder's own freestream authority begins and where it is complete, as
    // multiples of stall speed. An aircraft is designed to have full rudder control near
    // approach speed - holding the centreline in a crosswind and picking a wing up on
    // short final both depend on it - not at cruise. Sharing the wing's curve, which only
    // reaches full authority around two and a half times the stall, left the pedals vague
    // exactly where they matter most: slow, with the throttle back, where the slipstream
    // term below has nothing to contribute.
    private const float RudderFreestreamOnsetStallFraction = 0.45f;
    private const float RudderFreestreamFullStallFraction = 1.35f;

    // The fin keeps working as long as air is moving over it. Even a dead-stick glide
    // leaves usable rudder, so it never drops to the shared control-authority floor.
    private const float RudderMinimumAuthority = 0.45f;

    private static void UpdateStallAndAuthority(VehiclePlane plane, AircraftFlightState state)
    {
        var forwardSpeed = Mathf.Max(0f, state.ForwardSpeedMs);
        var lowSpeedAuthority = Mathf.InverseLerp(
            state.StallSpeedMs * 0.58f, state.ReferenceSpeedMs, forwardSpeed);
        var highSpeedLoss = Mathf.InverseLerp(
            state.MaximumSpeedMs * 0.82f, state.MaximumSpeedMs * 1.08f, state.AirspeedMs);
        var authority = lowSpeedAuthority * Mathf.Lerp(1f, 0.68f, highSpeedLoss);

        var angleSeverity = Mathf.InverseLerp(
            state.Profile.CriticalAngleOfAttack,
            state.Profile.CriticalAngleOfAttack + 11f,
            Mathf.Abs(state.AngleOfAttack));
        var speedSeverity = 1f - Mathf.InverseLerp(
            state.StallSpeedMs * 0.60f, state.StallSpeedMs * 1.08f, forwardSpeed);
        var targetStall = Settings.AircraftStallPhysicsEnabled.Value
            ? Mathf.Max(angleSeverity, speedSeverity * 0.82f)
            : 0f;
        var transitionRate = targetStall > state.StallSeverity ? 2.5f : 1.15f;
        state.StallSeverity = Mathf.MoveTowards(
            state.StallSeverity, targetStall, Time.fixedDeltaTime * transitionRate);

        var wasStalled = state.IsStalled;
        if (!state.IsStalled && state.StallSeverity >= 0.55f)
            state.IsStalled = true;
        else if (state.IsStalled && state.StallSeverity <= 0.22f)
            state.IsStalled = false;

        if (wasStalled != state.IsStalled)
            AiState.Trace($"Aircraft physics: {(state.IsStalled ? "stall entered" : "stall recovered")}");

        authority *= Mathf.Lerp(1f, 0.48f, state.StallSeverity);
        state.ControlAuthority = Mathf.Clamp(authority, 0.28f, 1f);

        // Keep the stall consequential without trapping the aircraft in an attitude from
        // which the native controls cannot unload the wing. Roll gets the largest reserve
        // because inverted recovery otherwise consumes more altitude than most ER2 maps allow.
        var recoveryBlend = Mathf.SmoothStep(
            0f, 1f, Mathf.InverseLerp(0.18f, 0.85f, state.StallSeverity));
        var pitchFloor = Mathf.Lerp(0.28f, AircraftTuning.StallRecoveryPitchAuthority, recoveryBlend);
        var rollFloor = Mathf.Lerp(0.28f, AircraftTuning.StallRecoveryRollAuthority, recoveryBlend);
        var yawFloor = Mathf.Lerp(0.28f, 0.52f, recoveryBlend);
        state.PitchAuthority = Mathf.Max(state.ControlAuthority, pitchFloor);
        state.RollAuthority = Mathf.Max(state.ControlAuthority, rollFloor);
        state.YawAuthority = Mathf.Max(YawAuthorityFor(plane, state, lowSpeedAuthority), yawFloor);
    }

    /// <summary>
    /// Rudder authority on its own curve rather than the wing's. The wing loses authority
    /// at low speed and keeps it at high speed; the rudder does close to the reverse,
    /// because the airflow over it comes from the propeller as much as from the freestream,
    /// while its usable deflection is limited by pedal force rather than by airflow.
    /// </summary>
    private static float YawAuthorityFor(
        VehiclePlane plane, AircraftFlightState state, float lowSpeedAuthority)
    {
        // Powered slipstream, lost with the propeller: a dead prop leaves the rudder with
        // only the freestream, which is exactly when a damaged aircraft should feel it.
        var slipstream = NormalizedThrottle(plane) * (1f - Mathf.Clamp01(state.PropellerLoss));

        // The rudder's freestream term is anchored to stall speed, not to the wing's
        // reference speed, so the pedals are fully alive by roughly a third above the
        // stall. lowSpeedAuthority is still taken as a lower bound so the rudder can
        // never come out weaker than the shared curve it used to follow.
        var freestream = Mathf.Max(
            lowSpeedAuthority,
            Mathf.InverseLerp(
                state.StallSpeedMs * RudderFreestreamOnsetStallFraction,
                state.StallSpeedMs * RudderFreestreamFullStallFraction,
                Mathf.Max(0f, state.ForwardSpeedMs)));
        var rudderAirflow = Mathf.Max(freestream, slipstream * RudderSlipstreamAuthority);

        var heaviness = Mathf.InverseLerp(
            state.MaximumSpeedMs * RudderHeavyOnsetSpeedFraction,
            state.MaximumSpeedMs,
            state.AirspeedMs);
        var forceLimit = Mathf.Lerp(1f, RudderAuthorityAtMaximumSpeed, heaviness);

        // A stalled aircraft still has propwash over the fin, so the stall penalty applies
        // to the rudder far more gently than to the wing surfaces — this is what lets a
        // pilot pick a wing up with rudder when the ailerons have gone dead.
        var stallPenalty = Mathf.Lerp(1f, 0.78f, state.StallSeverity);

        return Mathf.Clamp(
            rudderAirflow * forceLimit * stallPenalty, RudderMinimumAuthority, 1f);
    }

    private static void UpdateSpinState(
        VehiclePlane plane, Rigidbody rigidbody, AircraftFlightState state)
    {
        if (!Settings.AircraftStallPhysicsEnabled.Value ||
            Settings.AircraftPhysicsStrength.Value <= 0f ||
            AircraftTuning.SpinStrength <= 0f)
        {
            state.DeepStallTime = 0f;
            state.SpinRecoveryTime = 0f;
            state.IsSpinning = false;
            state.SpinSeverity = Mathf.MoveTowards(
                state.SpinSeverity, 0f, Time.fixedDeltaTime * 2.4f);
            if (state.SpinSeverity <= 0.01f)
                state.SpinDirection = 0f;
            return;
        }

        var speedRatio = Mathf.Max(0f, state.ForwardSpeedMs) /
                         Mathf.Max(1f, state.StallSpeedMs);
        var angleRatio = Mathf.Abs(state.AngleOfAttack) /
                         Mathf.Max(1f, state.Profile.CriticalAngleOfAttack);
        var deepStall = state.StallSeverity > 0.52f &&
                        (angleRatio > 0.90f || speedRatio < 0.98f);
        state.DeepStallTime = deepStall
            ? state.DeepStallTime + Time.fixedDeltaTime
            : Mathf.Max(0f, state.DeepStallTime - Time.fixedDeltaTime * 2f);

        if (!state.IsSpinning && state.DeepStallTime >= 0.18f)
        {
            var localOmegaDegrees =
                plane.transform.InverseTransformDirection(rigidbody.angularVelocity) * Mathf.Rad2Deg;
            var wingBias = BoolFloat(state.RightWingLost) - BoolFloat(state.LeftWingLost);
            var bias = Mathf.Clamp(state.SideslipAngle / 12f, -1f, 1f) +
                       Mathf.Clamp(localOmegaDegrees.y / 25f, -1f, 1f) * 0.50f -
                       Mathf.Clamp(localOmegaDegrees.z / 45f, -1f, 1f) * 0.35f +
                       wingBias * 0.75f;
            if (Mathf.Abs(bias) < 0.12f)
                bias = ((plane.GetInstanceID() & 1) == 0 ? 1f : -1f) * 0.12f;

            state.SpinDirection = Mathf.Sign(bias);
            state.SpinRecoveryTime = 0f;
            state.IsSpinning = true;
            AiState.Trace(
                $"Aircraft physics: spin entered direction={(state.SpinDirection > 0f ? "right" : "left")}");
        }

        if (state.IsSpinning)
        {
            var recovered = speedRatio >= AircraftTuning.SpinRecoverySpeed &&
                            angleRatio <= 0.78f;
            state.SpinRecoveryTime = recovered
                ? state.SpinRecoveryTime + Time.fixedDeltaTime
                : 0f;
            if (state.SpinRecoveryTime >= 0.15f)
            {
                state.IsSpinning = false;
                state.DeepStallTime = 0f;
                state.SpinRecoveryTime = 0f;
                AiState.Trace("Aircraft physics: spin recovered through forward airspeed and unloaded wing");
            }
        }

        var targetSpin = state.IsSpinning
            ? Mathf.Max(
                0.32f,
                Mathf.SmoothStep(
                    0f, 1f, Mathf.InverseLerp(0.48f, 1f, state.StallSeverity)))
            : 0f;
        state.SpinSeverity = Mathf.MoveTowards(
            state.SpinSeverity,
            targetSpin,
            Time.fixedDeltaTime * (targetSpin > state.SpinSeverity ? 1.20f : 1.80f));
        if (!state.IsSpinning && state.SpinSeverity <= 0.01f)
            state.SpinDirection = 0f;
    }

    private static void ResetSpinAndEnergyState(AircraftFlightState state)
    {
        state.DeepStallTime = 0f;
        state.SpinRecoveryTime = 0f;
        state.SpinSeverity = 0f;
        state.SpinDirection = 0f;
        state.IsSpinning = false;
        state.StallPitchBlend = 0f;
        state.StallPitchError = 0f;
        state.HasEnergySample = false;
        state.PreviousSpecificEnergy = 0f;
        state.PreviousEnergyPosition = Vector3.zero;
        state.MeasuredEnergyLossRate = 0f;
        state.EnergyRetentionAcceleration = 0f;
        state.ModeledDragAcceleration = 0f;
    }

    private static void ApplyEnergyRetention(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        Vector3 velocity)
    {
        var deltaTime = Mathf.Max(0.005f, Time.fixedDeltaTime);
        var energyPosition = rigidbody.position;
        var specificEnergy = velocity.sqrMagnitude * 0.5f -
                             Vector3.Dot(Physics.gravity, energyPosition);
        if (!state.HasEnergySample)
        {
            state.HasEnergySample = true;
            state.PreviousSpecificEnergy = specificEnergy;
            state.PreviousEnergyPosition = energyPosition;
            state.MeasuredEnergyLossRate = 0f;
            state.EnergyRetentionAcceleration = 0f;
            return;
        }

        // Do not turn a spawn, teleport, or network correction into several frames of
        // artificial thrust. Normal travel is roughly speed * dt; this threshold leaves
        // generous room for high-speed integration while rejecting discontinuities.
        var maximumContinuousTravel = Mathf.Max(8f, velocity.magnitude * deltaTime * 4f);
        if ((energyPosition - state.PreviousEnergyPosition).sqrMagnitude >
            maximumContinuousTravel * maximumContinuousTravel)
        {
            state.PreviousSpecificEnergy = specificEnergy;
            state.PreviousEnergyPosition = energyPosition;
            state.MeasuredEnergyLossRate = 0f;
            state.EnergyRetentionAcceleration = 0f;
            return;
        }

        var previousCompensation = state.EnergyRetentionAcceleration;
        state.MeasuredEnergyLossRate =
            (state.PreviousSpecificEnergy - specificEnergy) / deltaTime;
        state.PreviousSpecificEnergy = specificEnergy;
        state.PreviousEnergyPosition = energyPosition;

        var speed = state.AirspeedMs;
        // Inertia matters as soon as power is meaningfully reduced, not only at idle.
        // Fade the correction out near full throttle so normal powered acceleration
        // and high-power maneuvering remain governed by the native engine model.
        var throttleBlend = 1f - Mathf.InverseLerp(0.70f, 1f, NormalizedThrottle(plane));
        var validGlide = Settings.AircraftEnergyRetentionEnabled.Value &&
                         Settings.AircraftPhysicsStrength.Value > 0f &&
                         throttleBlend > 0f &&
                         IsStraightEnergyFlight(plane, rigidbody, state) &&
                         speed > 5f &&
                         state.ForwardSpeedMs > state.StallSpeedMs * 1.08f &&
                         state.StallSeverity < 0.26f &&
                         speed < state.MaximumSpeedMs * 1.06f;
        if (!validGlide)
        {
            state.EnergyRetentionAcceleration = 0f;
            return;
        }

        var normalizedAngle = Mathf.Abs(state.AngleOfAttack) /
                              Mathf.Max(1f, state.Profile.CriticalAngleOfAttack);
        var normalizedSlip = Mathf.Abs(state.SideslipAngle) / 20f;
        var liftToDragPenalty = 1f + normalizedAngle * normalizedAngle * 0.65f +
                                normalizedSlip * normalizedSlip * 0.80f;
        var effectiveLiftToDrag = Mathf.Max(
            4f, state.Profile.BestGlideLiftToDrag / liftToDragPenalty);
        var cleanGlideDeceleration = Physics.gravity.magnitude / effectiveLiftToDrag *
                                     AircraftTuning.GlideEnergyLoss;
        var gearAllowance = plane.gear_opened ? 1.25f : 0f;
        var damageAllowance =
            (BoolFloat(state.LeftWingLost) + BoolFloat(state.RightWingLost)) * 0.80f +
            state.TailLoss * 0.55f + state.PropellerLoss * 0.20f;
        var allowedDeceleration = cleanGlideDeceleration + state.ModeledDragAcceleration +
                                  gearAllowance + damageAllowance;

        // The measured energy already includes last frame's corrective force. Add its
        // power back before estimating native loss, which prevents a one-frame on/off pulse.
        var uncompensatedLossRate = state.MeasuredEnergyLossRate +
                                    speed * previousCompensation;
        var excessiveLossRate = uncompensatedLossRate - speed * allowedDeceleration;
        var targetAcceleration = Mathf.Clamp(
            excessiveLossRate / Mathf.Max(5f, speed),
            0f,
            AircraftTuning.MaximumEnergyRetentionAcceleration) *
            throttleBlend * Mathf.Clamp01(Settings.AircraftPhysicsStrength.Value);
        var smoothing = 1f - Mathf.Exp(-14f * deltaTime);
        state.EnergyRetentionAcceleration = Mathf.Lerp(
            previousCompensation, targetAcceleration, smoothing);

        if (state.EnergyRetentionAcceleration > 0.005f)
        {
            rigidbody.AddForce(
                velocity.normalized * state.EnergyRetentionAcceleration,
                ForceMode.Acceleration);
        }
    }

    private static void ApplyAerodynamicCorrections(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        Vector3 velocity,
        Vector3 localVelocity)
    {
        var strength = Settings.AircraftPhysicsStrength.Value;
        var energy = AircraftTuning.EnergyLoss;
        if (strength <= 0f)
        {
            state.ModeledDragAcceleration = 0f;
            state.StallPitchBlend = 0f;
            state.StallPitchError = 0f;
            return;
        }

        var speed = Mathf.Max(0.1f, state.AirspeedMs);
        var dynamicPressure = Mathf.Clamp(
            speed * speed / (state.ReferenceSpeedMs * state.ReferenceSpeedMs), 0f, 4f);
        var normalizedAoA = Mathf.Abs(state.AngleOfAttack) /
                            Mathf.Max(1f, state.Profile.CriticalAngleOfAttack);
        GetManeuverDemands(plane, rigidbody, state, out var bankDemand, out var turnRateDemand);
        var overspeed = Mathf.InverseLerp(
            state.MaximumSpeedMs * 0.92f, state.MaximumSpeedMs * 1.08f, speed);

        var parasiteDrag = state.Profile.ParasiteDrag * dynamicPressure;
        var inducedDrag = state.Profile.InducedDrag * normalizedAoA * normalizedAoA * dynamicPressure;
        // Bank angle represents the additional lift needed to hold altitude, while local
        // pitch/yaw rate catches hard pull-throughs and skidding turns. Roll rate alone is
        // excluded so entering or leaving a bank does not create a spurious speed pulse.
        inducedDrag += bankDemand * 0.85f * dynamicPressure;
        inducedDrag += turnRateDemand * turnRateDemand * 1.35f * dynamicPressure;
        var stallDrag = state.Profile.StallDrag * state.StallSeverity * state.StallSeverity;
        var gearDrag = plane.gear_opened ? state.Profile.LandingGearDrag * dynamicPressure : 0f;
        var overspeedDrag = overspeed * overspeed * 4.5f;
        var damageDrag = (BoolFloat(state.LeftWingLost) + BoolFloat(state.RightWingLost)) * 1.4f +
                         state.TailLoss * 0.75f + state.PropellerLoss * 0.55f;
        var totalDrag = (parasiteDrag + inducedDrag + stallDrag + gearDrag + overspeedDrag + damageDrag) *
                        energy * strength;
        var lateralAccelerationMagnitude = Mathf.Abs(localVelocity.x) *
                                           state.Profile.SideslipDamping * dynamicPressure *
                                           energy * strength;
        state.ModeledDragAcceleration = totalDrag +
                                         lateralAccelerationMagnitude * Mathf.Abs(localVelocity.x) / speed;
        rigidbody.AddForce(-velocity.normalized * totalDrag, ForceMode.Acceleration);

        var lateralAcceleration = -plane.transform.right * localVelocity.x *
                                  state.Profile.SideslipDamping * dynamicPressure * energy * strength;
        rigidbody.AddForce(lateralAcceleration, ForceMode.Acceleration);

        if (Settings.AircraftStallPhysicsEnabled.Value && state.StallSeverity > 0f)
        {
            var sinkFade = Mathf.Lerp(1f, 0.55f, state.SpinSeverity);
            rigidbody.AddForce(
                Vector3.down * Physics.gravity.magnitude * state.Profile.StallSinkGravity *
                state.StallSeverity * sinkFade * strength,
                ForceMode.Acceleration);
            ApplyStallMoments(plane, rigidbody, state, velocity, strength);
        }
        else
        {
            state.StallPitchBlend = 0f;
            state.StallPitchError = 0f;
        }

        if (Settings.AircraftDamagePhysicsEnabled.Value)
            ApplyDamageForces(plane, rigidbody, state, dynamicPressure, strength);

        var unstableBlend = Mathf.Max(state.StallSeverity, state.SpinSeverity);
        var damping = state.Profile.AngularDamping * Mathf.Lerp(1f, 0.25f, unstableBlend);
        rigidbody.AddTorque(
            -rigidbody.angularVelocity * damping * strength, ForceMode.Acceleration);
    }

    private static bool IsStraightEnergyFlight(
        VehiclePlane plane, Rigidbody rigidbody, AircraftFlightState state)
    {
        GetManeuverDemands(plane, rigidbody, state, out var bankDemand, out var turnRateDemand);
        return bankDemand < 0.08f &&
               turnRateDemand < 0.18f &&
               Mathf.Abs(state.SideslipAngle) < 6f;
    }

    private static void GetManeuverDemands(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        out float bankDemand,
        out float turnRateDemand)
    {
        bankDemand = 1f - Mathf.Abs(Vector3.Dot(plane.transform.up, Vector3.up));
        var localAngularDegrees =
            plane.transform.InverseTransformDirection(rigidbody.angularVelocity) * Mathf.Rad2Deg;
        var pitchDemand = Mathf.Abs(localAngularDegrees.x) /
                          Mathf.Max(1f, state.Profile.MaximumPitchRate);
        var yawDemand = Mathf.Abs(localAngularDegrees.y) /
                        Mathf.Max(1f, state.Profile.MaximumYawRate);
        turnRateDemand = Mathf.Clamp01(Mathf.Sqrt(
            pitchDemand * pitchDemand + yawDemand * yawDemand));
    }

    private static void ApplyStallMoments(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        Vector3 velocity,
        float strength)
    {
        state.StallPitchBlend = Mathf.SmoothStep(
            0f, 1f, Mathf.InverseLerp(0.25f, 0.80f, state.StallSeverity));

        var flightPath = velocity.sqrMagnitude > 9f ? velocity.normalized : Vector3.down;
        var upwardFlight = Mathf.Clamp01(Vector3.Dot(flightPath, Vector3.up));
        var downBias = Mathf.Lerp(0.25f, 1.25f, state.StallSeverity) +
                       upwardFlight * 0.65f * state.StallSeverity;
        var desiredDirection = flightPath + Vector3.down * downBias;
        if (desiredDirection.sqrMagnitude > 0.01f)
            desiredDirection.Normalize();
        else
            desiredDirection = Vector3.down;

        var pitchTarget = Vector3.ProjectOnPlane(desiredDirection, plane.transform.right);
        if (pitchTarget.sqrMagnitude > 0.01f)
        {
            pitchTarget.Normalize();
            state.StallPitchError = Mathf.Clamp(
                Vector3.SignedAngle(
                    plane.transform.forward, pitchTarget, plane.transform.right),
                -100f,
                100f);
        }
        else
        {
            state.StallPitchError = 0f;
        }

        var localOmega = plane.transform.InverseTransformDirection(rigidbody.angularVelocity);
        var noseDropStrength = AircraftTuning.StallNoseDropStrength;
        var spinStrength = AircraftTuning.SpinStrength;
        var targetPitch = Mathf.Clamp(state.StallPitchError / 45f, -1f, 1f) *
                          state.Profile.StallPitchRate * Mathf.Deg2Rad *
                          state.StallPitchBlend * noseDropStrength;
        var targetYaw = state.SpinDirection * state.Profile.SpinYawRate * Mathf.Deg2Rad *
                        state.SpinSeverity * spinStrength;
        var targetRoll = -state.SpinDirection * state.Profile.SpinRollRate * Mathf.Deg2Rad *
                         state.SpinSeverity * spinStrength;

        var pitchAcceleration = noseDropStrength > 0f
            ? Mathf.Clamp(
                (targetPitch - localOmega.x) * 2.8f,
                -2.6f * Mathf.Max(0.35f, noseDropStrength),
                2.6f * Mathf.Max(0.35f, noseDropStrength))
            : 0f;
        var yawAcceleration = spinStrength > 0f
            ? Mathf.Clamp(
                (targetYaw - localOmega.y) * 3.2f,
                -2.2f * Mathf.Max(0.35f, spinStrength),
                2.2f * Mathf.Max(0.35f, spinStrength))
            : 0f;
        var rollAcceleration = spinStrength > 0f
            ? Mathf.Clamp(
                (targetRoll - localOmega.z) * 3f,
                -3.4f * Mathf.Max(0.35f, spinStrength),
                3.4f * Mathf.Max(0.35f, spinStrength))
            : 0f;

        var localAcceleration = new Vector3(
            pitchAcceleration, yawAcceleration, rollAcceleration);
        rigidbody.AddTorque(
            plane.transform.TransformDirection(localAcceleration) * strength,
            ForceMode.Acceleration);
    }

    private static void ApplyDamageForces(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        float dynamicPressure,
        float strength)
    {
        var wingLosses = BoolFloat(state.LeftWingLost) + BoolFloat(state.RightWingLost);
        if (wingLosses > 0f)
        {
            rigidbody.AddForce(
                Vector3.down * Physics.gravity.magnitude * 0.34f * wingLosses * strength,
                ForceMode.Acceleration);

            var imbalance = BoolFloat(state.RightWingLost) - BoolFloat(state.LeftWingLost);
            if (Mathf.Abs(imbalance) > 0f)
            {
                rigidbody.AddTorque(
                    plane.transform.forward * imbalance * state.Profile.MissingWingRollAcceleration *
                    Mathf.Clamp(dynamicPressure, 0.25f, 2f) * strength,
                    ForceMode.Acceleration);
            }
        }

        if (state.TailLoss > 0f)
        {
            var localAngularVelocity = plane.transform.InverseTransformDirection(rigidbody.angularVelocity);
            var destabilizingTorque = new Vector3(
                localAngularVelocity.x * 0.18f,
                localAngularVelocity.y * 0.24f,
                0f) * state.TailLoss * strength;
            rigidbody.AddTorque(
                plane.transform.TransformDirection(destabilizingTorque), ForceMode.Acceleration);
        }
    }

    private static void LimitAngularVelocity(
        VehiclePlane plane, Rigidbody rigidbody, AircraftFlightState state)
    {
        var strength = Settings.AircraftPhysicsStrength.Value;
        if (strength <= 0f)
            return;

        var blend = Mathf.Clamp01(strength);
        var local = plane.transform.InverseTransformDirection(rigidbody.angularVelocity);
        var tailPitchControl = Mathf.Lerp(1f, 1f - state.TailLoss * 0.55f, blend);
        var tailYawControl = Mathf.Lerp(1f, 1f - state.TailLoss * 0.65f, blend);
        var wingControl = Mathf.Lerp(
            1f, 1f - (BoolFloat(state.LeftWingLost) + BoolFloat(state.RightWingLost)) * 0.28f, blend);
        var pitchLimit = Mathf.Lerp(360f, state.Profile.MaximumPitchRate * state.PitchAuthority, blend) *
                         tailPitchControl * Mathf.Deg2Rad;
        var yawLimit = Mathf.Lerp(360f, state.Profile.MaximumYawRate * state.YawAuthority, blend) *
                       tailYawControl * Mathf.Deg2Rad;
        var rollLimit = Mathf.Lerp(360f, state.Profile.MaximumRollRate * state.RollAuthority, blend) *
                        wingControl * Mathf.Deg2Rad;
        var noseDropLimit = state.Profile.StallPitchRate * state.StallPitchBlend *
                            AircraftTuning.StallNoseDropStrength * 1.15f * Mathf.Deg2Rad;
        var spinYawLimit = state.Profile.SpinYawRate * state.SpinSeverity *
                           AircraftTuning.SpinStrength * 1.15f * Mathf.Deg2Rad;
        var spinRollLimit = state.Profile.SpinRollRate * state.SpinSeverity *
                            AircraftTuning.SpinStrength * 1.15f * Mathf.Deg2Rad;
        pitchLimit = Mathf.Max(pitchLimit, noseDropLimit);
        yawLimit = Mathf.Max(yawLimit, spinYawLimit);
        rollLimit = Mathf.Max(rollLimit, spinRollLimit);
        local.x = Mathf.Clamp(local.x, -pitchLimit, pitchLimit);
        local.y = Mathf.Clamp(local.y, -yawLimit, yawLimit);
        local.z = Mathf.Clamp(local.z, -rollLimit, rollLimit);
        rigidbody.angularVelocity = plane.transform.TransformDirection(local);
    }

    private static void UpdateDamageState(VehiclePlane plane, AircraftFlightState state)
    {
        if (!Settings.AircraftDamagePhysicsEnabled.Value)
        {
            state.LeftWingLost = false;
            state.RightWingLost = false;
            state.TailLoss = 0f;
            state.PropellerLoss = 0f;
            return;
        }

        state.LeftWingLost = IsDetached(plane.leftDetachableWing);
        state.RightWingLost = IsDetached(plane.rightDetachableWing);
        state.TailLoss = DetachedFraction(plane.detachableTails);
        state.PropellerLoss = DetachedFraction(plane.detachablePropellers);
    }

    private static void ApplyNativeTuning(VehiclePlane plane, AircraftFlightState state)
    {
        var strength = Settings.AircraftPhysicsStrength.Value;
        var blend = Mathf.Clamp01(strength);
        var speedScale = Mathf.Lerp(1f, ConfiguredSpeedScale(state), blend);
        state.StallSpeedMs = state.BaseStallSpeedMs * speedScale;
        state.ReferenceSpeedMs = state.BaseReferenceSpeedMs * speedScale;
        state.MaximumSpeedMs = state.BaseMaximumSpeedMs * speedScale;

        plane.clocheMultiplier = state.OriginalClocheMultiplier * Mathf.Lerp(
            1f, AircraftTuning.ControlResponse, blend);
        plane.timeFromZeroToMaxThrust = state.OriginalThrustResponseSeconds * Mathf.Lerp(
            1f, AircraftTuning.EngineResponse, blend);
        plane.maxKmhSpeed = state.OriginalMaximumSpeedKmh * speedScale;

        // Lift is proportional to speed squared. Inverse-square scaling keeps takeoff,
        // stall, and level-flight behavior aligned with the reduced world-speed envelope.
        var inverseSpeedSquared = 1f / Mathf.Max(0.25f, speedScale * speedScale);
        plane.startLiftMult = state.OriginalStartLiftMultiplier * inverseSpeedSquared;
        plane.endLiftMult = state.OriginalEndLiftMultiplier * inverseSpeedSquared;

        var propellerPower = Settings.AircraftDamagePhysicsEnabled.Value
            ? Mathf.Lerp(1f, 0.08f, state.PropellerLoss)
            : 1f;
        var propellerScale = Mathf.Lerp(1f, propellerPower, blend);
        var enginePower = Mathf.Lerp(1f, Settings.AircraftEnginePowerMultiplier.Value, blend);
        var engineScale = propellerScale * enginePower;
        var uncappedMultiplier = state.OriginalThrustForceMultiplier *
                                 speedScale * speedScale * engineScale;

        var rigidbody = plane.GetRigidbody();
        if (blend <= 0f || rigidbody == null || rigidbody.mass <= 0.1f)
        {
            state.AvailableEngineAccelerationMs2 = 0f;
            plane.thrustForceMultiplier = uncappedMultiplier;
            return;
        }

        // Stock thrust was balanced against a per-tick velocity subtraction. Once that
        // artificial loss is removed, several aircraft can produce more than their own
        // weight in thrust and accelerate forever in a vertical climb. Treat the engine
        // as a propeller: static thrust is sub-1:1 and available thrust falls with speed
        // at approximately constant shaft power.
        var staticThrustToWeight = state.Profile.Name == "bomber" ? 0.38f : 0.58f;
        var maximumSpeedDynamicPressure = Mathf.Clamp(
            state.MaximumSpeedMs * state.MaximumSpeedMs /
            Mathf.Max(1f, state.ReferenceSpeedMs * state.ReferenceSpeedMs), 0f, 4f);
        var modeledDragAtMaximumSpeed =
            state.Profile.ParasiteDrag * maximumSpeedDynamicPressure + 1.125f;
        var specificPropulsivePower = modeledDragAtMaximumSpeed * 1.10f *
                                      state.MaximumSpeedMs;
        var propellerSpeedFloor = Mathf.Max(12f, state.StallSpeedMs * 0.55f);
        var powerLimitedAcceleration = specificPropulsivePower /
                                       Mathf.Max(propellerSpeedFloor, rigidbody.velocity.magnitude);
        var staticLimitedAcceleration = Physics.gravity.magnitude * staticThrustToWeight;
        var physicalAcceleration = Mathf.Min(
            Mathf.Min(staticLimitedAcceleration, powerLimitedAcceleration) * engineScale,
            Physics.gravity.magnitude * 0.82f);
        var physicalMultiplier = rigidbody.mass * physicalAcceleration / NativeMaximumThrottle;
        var cappedMultiplier = Mathf.Min(uncappedMultiplier, physicalMultiplier);
        var appliedMultiplier = Mathf.Lerp(uncappedMultiplier, cappedMultiplier, blend);

        plane.thrustForceMultiplier = appliedMultiplier;
        state.AvailableEngineAccelerationMs2 =
            appliedMultiplier * NativeMaximumThrottle / rigidbody.mass;

        // Clamp stale force from the previous tuning frame without tying current thrust
        // to the throttle command; native spool-down remains free to decay gradually.
        var maximumPhysicalForce = appliedMultiplier * NativeMaximumThrottle;
        plane.thrustForce = Mathf.Min(plane.thrustForce, maximumPhysicalForce);
        var targetForceAtThrottle = appliedMultiplier *
                                    Mathf.Clamp(plane.throttle, 0f, NativeMaximumThrottle);
        plane._trgtThrust = Mathf.Min(plane._trgtThrust, targetForceAtThrottle);
    }

    private static float ConfiguredSpeedScale(AircraftFlightState state)
    {
        var typeMultiplier = state.Profile.Name == "bomber"
            ? Settings.AircraftBomberSpeedMultiplier.Value
            : Settings.AircraftFighterSpeedMultiplier.Value;
        return Mathf.Clamp(Settings.AircraftWorldSpeedScale.Value * typeMultiplier, 0.50f, 1.75f);
    }

    /// <summary>
    /// An aircraft nobody is flying is still an aircraft: it holds the energy it had and
    /// carries on until drag and gravity bring it down. Easy Red 2 releases a pilotless
    /// plane from its flight handling the moment the seat empties, which reads as the
    /// aircraft stopping in mid-air and dropping instead of flying away from the pilot who
    /// just left it. Only the HORIZONTAL component is defended here — vertical speed is
    /// supposed to change, that is the aeroplane descending.
    /// </summary>
    private sealed class AbandonedCoast
    {
        internal Vector3 Velocity;
        internal float Until;
    }

    private static readonly Dictionary<int, AbandonedCoast> Abandoned = new();

    // Long enough to carry a bailed-out aircraft well clear of its pilot and into the
    // ground on its own, without tracking wrecks indefinitely.
    private const float AbandonedCoastSeconds = 30f;

    // Speed bled per second with no thrust. A clean airframe coasting loses energy
    // steadily rather than stopping, so this is a glide, not a brake.
    private const float AbandonedDragPerSecond = 0.06f;

    // No aerodynamic deceleration removes a quarter of the aircraft's horizontal speed in
    // a single physics tick. Anything sharper is the native controller letting go, which
    // is precisely what this exists to undo.
    private const float ImplausibleSpeedLossFraction = 0.25f;

    private static void TryBeginAbandonedCoast(VehiclePlane plane, int id)
    {
        try
        {
            // Only a genuinely empty cockpit coasts. ShouldApply also goes false for an
            // AI pilot, a control-scheme change, or the feature being switched off, and
            // none of those mean the aircraft was abandoned.
            if (plane.GetDriver() != null || plane.isGrounded)
                return;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || rigidbody.isKinematic)
                return;

            Abandoned[id] = new AbandonedCoast
            {
                Velocity = rigidbody.velocity,
                Until = Time.time + AbandonedCoastSeconds
            };
        }
        catch
        {
            // The plane may already be despawning; there is nothing to coast.
        }
    }

    private static void MaintainAbandonedMomentum(VehiclePlane plane, int id)
    {
        if (!Abandoned.TryGetValue(id, out var coast))
            return;

        try
        {
            if (Time.time >= coast.Until || plane.isGrounded || plane.GetDriver() != null)
            {
                Abandoned.Remove(id);
                return;
            }

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || rigidbody.isKinematic)
            {
                Abandoned.Remove(id);
                return;
            }

            var velocity = rigidbody.velocity;
            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            var expected = new Vector3(coast.Velocity.x, 0f, coast.Velocity.z) *
                           (1f - AbandonedDragPerSecond * Time.fixedDeltaTime);

            if (horizontal.magnitude >= expected.magnitude)
            {
                // Native physics is carrying it fine, or it is diving and gaining speed.
                coast.Velocity = velocity;
                return;
            }

            if (expected.sqrMagnitude > 1f &&
                horizontal.magnitude < expected.magnitude * (1f - ImplausibleSpeedLossFraction))
            {
                rigidbody.velocity = new Vector3(expected.x, velocity.y, expected.z);
                coast.Velocity = rigidbody.velocity;
                return;
            }

            coast.Velocity = new Vector3(expected.x, velocity.y, expected.z);
        }
        catch
        {
            Abandoned.Remove(id);
        }
    }

    private static void RestoreNativeValues(VehiclePlane plane, AircraftFlightState state)
    {
        try
        {
            plane.clocheMultiplier = state.OriginalClocheMultiplier;
            plane.timeFromZeroToMaxThrust = state.OriginalThrustResponseSeconds;
            plane.thrustForceMultiplier = state.OriginalThrustForceMultiplier;
            plane.maxKmhSpeed = state.OriginalMaximumSpeedKmh;
            plane.startLiftMult = state.OriginalStartLiftMultiplier;
            plane.endLiftMult = state.OriginalEndLiftMultiplier;
        }
        catch
        {
            // The native object may already be in its destruction path.
        }
    }

    private static void WriteTelemetry(VehiclePlane plane, AircraftFlightState state)
    {
        if (!Settings.AircraftPhysicsTelemetryEnabled.Value || Time.unscaledTime < state.NextTelemetry)
            return;

        state.NextTelemetry = Time.unscaledTime + Settings.AircraftPhysicsTelemetryInterval.Value;
        var clearance = TerrainClearanceAhead(plane, state);
        var rigidbody = plane.GetRigidbody();
        var rigidbodyDrag = rigidbody != null ? rigidbody.drag : -1f;
        Plugin.LogSource.LogInfo(
            $"[Aircraft telemetry] {SafeName(plane)} profile={state.Profile.Name} " +
            $"speed={state.AirspeedMs * 3.6f:0}km/h forward={state.ForwardSpeedMs * 3.6f:0}km/h " +
            $"vertical={state.VerticalSpeedMs:0.0}m/s aoa={state.AngleOfAttack:0.0}deg " +
            $"slip={state.SideslipAngle:0.0}deg authority=P{state.PitchAuthority:0.00}/R{state.RollAuthority:0.00}/Y{state.YawAuthority:0.00} " +
            $"stall={state.StallSeverity:0.00} spin={state.SpinSeverity:0.00}/{state.SpinDirection:+0;-0;0} " +
            $"pitchError={state.StallPitchError:0}deg energyLoss={state.MeasuredEnergyLossRate:0}W/kg " +
            $"retention={state.EnergyRetentionAcceleration:0.00}m/s2 nativeRestore={state.NativeVelocityCorrectionMs:0.00}m/s " +
            $"throttle={NormalizedThrottle(plane) * 100f:0}% " +
            $"thrust={plane.thrustForce:0}/{plane._trgtThrust:0} rbDrag={rigidbodyDrag:0.000} " +
            $"enginePower={Settings.AircraftEnginePowerMultiplier.Value:0.00} " +
            $"engineLimit={state.AvailableEngineAccelerationMs2:0.00}m/s2 " +
            $"coastScale={AircraftTuning.NativeCoastDrag:0.00} " +
            $"nativeVelocityScale={AircraftTuning.NativeVelocityLoss:0.00} " +
            $"agl={(clearance >= 0f ? clearance.ToString("0") : "unknown")}m " +
            $"wingL={(state.LeftWingLost ? "lost" : "ok")} wingR={(state.RightWingLost ? "lost" : "ok")} " +
            $"tailLoss={state.TailLoss:0.00} propLoss={state.PropellerLoss:0.00}");
    }

    private static bool ShouldApply(VehiclePlane plane)
    {
        if (!Settings.AircraftFlightPhysicsEnabled.Value ||
            !MultiplayerAuthority.CanMutateGameplay())
        {
            return false;
        }

        try
        {
            // AI aircraft deliberately fly the vanilla flight model. This is structural,
            // not a setting: there is no configuration in which an AI-driven plane
            // reaches the physics below. The question here is who flies the plane, not
            // whether the mod may steer the pilot, so an AI pilot in a player's squad
            // must still be caught by the control-flag test alone.
            var driver = plane.GetDriver();
            if (driver == null || AiOwnership.IsAiControlled(driver))
                return false;

            // Simplified flight uses VehiclePlane.targetRotation as an absolute
            // steering target. The force/velocity corrections below are designed for
            // the two realistic controllers and can fight that target between Update
            // and FixedUpdate until the aircraft appears unresponsive. Keep the native
            // simplified controller intact; switching control mode while seated will
            // automatically restore or reinitialize this state on the next physics tick.
            if (!plane.PlayerIsDrivingWithRealisticControls())
                return false;

            return Lua_API.isOnline()
                ? Settings.AircraftPhysicsApplyToMultiplayerPlayers.Value
                : Settings.AircraftPhysicsApplyToOfflinePlayers.Value;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDetached(VehicleDamagableDetachablePart? part)
    {
        try
        {
            return part != null && part.IsDetached();
        }
        catch
        {
            return false;
        }
    }

    private static float DetachedFraction(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<VehicleDamagableDetachablePart>? parts)
    {
        if (parts == null || parts.Length == 0)
            return 0f;

        var configured = 0;
        var detached = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == null)
                continue;

            configured++;
            if (IsDetached(part))
                detached++;
        }

        return configured > 0 ? detached / (float)configured : 0f;
    }

    private static bool ValidPositive(float value)
        => value > 0.01f && !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool FiniteRotation(Quaternion rotation)
        => !float.IsNaN(rotation.x) && !float.IsInfinity(rotation.x) &&
           !float.IsNaN(rotation.y) && !float.IsInfinity(rotation.y) &&
           !float.IsNaN(rotation.z) && !float.IsInfinity(rotation.z) &&
           !float.IsNaN(rotation.w) && !float.IsInfinity(rotation.w);

    private static float NormalizedThrottle(VehiclePlane plane)
        => Mathf.Clamp01(plane.throttle / NativeMaximumThrottle);

    private static float BoolFloat(bool value) => value ? 1f : 0f;

    internal static string SafeName(VehiclePlane plane)
    {
        try
        {
            return $"{plane.name}#{plane.GetInstanceID()}";
        }
        catch
        {
            return "plane";
        }
    }
}

[HarmonyPatch(typeof(VehiclePlane), "Start")]
internal static class AircraftFlightInitializationPatch
{
    [HarmonyPostfix]
    private static void Postfix(VehiclePlane __instance)
        => AircraftFlightPhysics.Initialize(__instance);
}

[HarmonyPatch(typeof(VehiclePlane), "FixedUpdate")]
internal static class AircraftFlightFixedUpdatePatch
{
    [HarmonyPrefix]
    private static void Prefix(VehiclePlane __instance, out float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            __state = AircraftFlightPhysics.BeginNativeFixedUpdate(__instance);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(VehiclePlane __instance, float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            AircraftFlightPhysics.EndNativeFixedUpdate(__state);
            AircraftFlightPhysics.FixedUpdate(__instance);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(float __state)
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            AircraftFlightPhysics.EndNativeFixedUpdate(__state);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }
}

[HarmonyPatch(typeof(VehiclePlane), nameof(VehiclePlane.OpenGear))]
internal static class AircraftLandingGearSpeedInterlockPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VehiclePlane __instance)
        => AircraftFlightPhysics.AllowLandingGearExtension(__instance);
}

[HarmonyPatch(typeof(VehiclePlane), "RefreshThrustForce", new[] { typeof(bool) })]
internal static class AircraftThrottleReductionPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance, ref bool canApplyThrust, out float __state)
    {
        AircraftFlightPhysics.DecoupleThrottleSpeedGovernor(
            __instance, ref canApplyThrust);
        try
        {
            __state = __instance != null ? __instance.thrustForce : 0f;
        }
        catch
        {
            __state = 0f;
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        VehiclePlane __instance, bool canApplyThrust, float __state)
        => AircraftFlightPhysics.SmoothNativeThrottleReduction(
            __instance, canApplyThrust, __state);
}

[HarmonyPatch(
    typeof(RigidbodyCompat), nameof(RigidbodyCompat.SetVelocity),
    new[] { typeof(Rigidbody), typeof(Vector3) })]
internal static class AircraftNativeVelocityLossPatch
{
    [HarmonyPrefix]
    private static void Prefix(Rigidbody rb, ref Vector3 velocity)
        => AircraftFlightPhysics.ReplaceNativeVelocityLoss(rb, ref velocity);
}

/// <summary>
/// While the vehicle freelook button is held, the native realistic-plane control block in
/// PlayerController.Update takes a dedicated branch that calls
/// RotateRealisticJoystick(yaw, 0f, 0f): pitch and roll are discarded outright, so the aircraft
/// can only yaw for as long as the player is looking around.
///
/// The freelook camera is always the RIGHT stick: GetRotationInput picks GetCameraRotationInput
/// while the button is held, and that passes swapSticks: false, which selects RightStickY/X. The
/// left stick is therefore never the camera, in either stick layout, and is the one place flight
/// input can be put during a look without the camera flying the aeroplane.
///
/// So while freelook is held on a physical controller the left stick flies: pitch from its Y axis,
/// roll from its X axis, read through the game's own GetGenericCameraRotationInput with swapSticks
/// forced true so the invert-Y, sensitivity and acceleration all match the native feel instead of
/// approximating it. The physical-controller guard is essential because VirtualGamepad otherwise
/// exposes W/S and A/D as simulated left-stick axes while the keyboard is assigned.
///
/// Two things otherwise share that stick and have to yield for the duration of the hold:
///   * Rudder. With the sticks unswapped the native yaw is GetAxis(LeftStickX) — the same axis now
///     giving roll — so yaw is zeroed. With them swapped, yaw is on RightStickX and is left alone.
///   * Throttle, which is driven by GetButton(realisticPlane_throttle) and can sit on the same
///     stick. It is held at its current value only while the pitch axis is actually deflected, so
///     the player can still change throttle mid-look whenever they are not pitching.
/// </summary>
internal static class AircraftFreeLookSteering
{
    // Deflection past which the pitch axis is considered to be driving the stick, and therefore
    // possibly the throttle bound to the same physical direction.
    private const float PitchAxisDeadZone = 0.01f;

    private static bool _loggedFailure;
    private static bool _holdingThrottle;
    private static float _heldThrottle;

    internal static void RestoreSuppressedInputs(
        VehiclePlane plane, ref float yaw, ref float pitch, ref float roll)
    {
        if (!Settings.AircraftFreeLookSteeringEnabled.Value)
            return;

        // Only the native branch's hardcoded zeroes are replaced. Anything the game actually
        // supplied is the player's own input and is left exactly as it arrived.
        if (pitch != 0f || roll != 0f)
            return;

        try
        {
            var controller = PlayerController.currentController;
            if (!IsFreeLookHeld() || controller == null || plane == null ||
                controller.ControlledVehicle?.GetInstanceID() != plane.GetInstanceID())
            {
                _holdingThrottle = false;
                return;
            }

            // The native plane path reaches the same helper as
            // GetGenericCameraRotationInput(YAxisDirection_plane * -1, 1, InvertPlaneSticks), but
            // its right stick and this left stick report opposite vertical polarity, so pushing
            // forward on one is pushing back on the other. The sign is corrected here rather than
            // by negating the result, because YaxisDirection scales only the vertical component:
            // pitch follows the player's invert setting exactly as it does on the right stick,
            // and roll is left untouched.
            var input = PlayerController.GetGenericCameraRotationInput(
                PlaneYAxisDirection(), 1, swapSticks: true);
            var multiplier = PlayerController.InputUpdateMultiplier() * controller.AimMultiplier();
            pitch = input.x * multiplier;
            roll = input.y * multiplier;

            // Unswapped layout: the native rudder is the same LeftStickX that is now giving roll,
            // so one physical axis would otherwise do two jobs at once.
            if (!PlayerController.InvertPlaneSticks)
                yaw = 0f;

            HoldThrottleWhilePitching(plane, input.x);
        }
        catch (Exception ex)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Plugin.LogSource.LogWarning(
                $"Freelook plane steering disabled after an input failure: {ex.Message}");
        }
    }

    /// <summary>
    /// The throttle buttons are read earlier in the same PlayerController.Update pass than the
    /// rotation call this runs from, so re-asserting the captured value here corrects it within
    /// the same frame, before FixedUpdate turns it into thrust.
    /// </summary>
    private static void HoldThrottleWhilePitching(VehiclePlane plane, float pitchAxis)
    {
        if (Mathf.Abs(pitchAxis) <= PitchAxisDeadZone)
        {
            // Not pitching, so nothing of ours can be moving the throttle: let the player's own
            // throttle changes through and keep tracking them.
            _holdingThrottle = false;
            return;
        }

        if (!_holdingThrottle)
        {
            _holdingThrottle = true;
            _heldThrottle = plane.throttle;
            return;
        }

        plane.throttle = _heldThrottle;
    }

    private static int PlaneYAxisDirection()
    {
        var controls = SavableData.Settings?.controls;
        return controls != null ? controls.YAxisDirection_plane : 1;
    }

    private static bool IsFreeLookHeld()
    {
        var gamepad = GamepadsAPI.GetGamepad(0);
        return gamepad != null &&
               gamepad.IsGamepad &&
               gamepad.GetButton(
                   GameInput.LookAroundInVehicle, StickPressCondition.StickCentered);
    }
}

[HarmonyPatch(typeof(VehiclePlane), nameof(VehiclePlane.RotateRealisticJoystick))]
internal static class AircraftJoystickAuthorityPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance, ref float yaw, ref float pitch, ref float roll)
    {
        // Restore first, scale second: input recovered from the freelook branch still has to pass
        // through the stall and battle-damage authority limits like any other stick deflection.
        AircraftFreeLookSteering.RestoreSuppressedInputs(
            __instance, ref yaw, ref pitch, ref roll);
        AircraftFlightPhysics.ScalePlayerInputs(__instance, ref yaw, ref pitch, ref roll);
    }
}

[HarmonyPatch(typeof(VehiclePlane), nameof(VehiclePlane.RotateRealisticMouse))]
internal static class AircraftMouseAuthorityPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance, ref float yaw, ref float add_pitch, ref float add_roll)
        => AircraftFlightPhysics.ScalePlayerInputs(
            __instance, ref yaw, ref add_pitch, ref add_roll);
}

[HarmonyPatch(typeof(Vehicle), "OnDestroy")]
internal static class AircraftFlightStateCleanupPatch
{
    [HarmonyPrefix]
    private static void Prefix(Vehicle __instance)
    {
        try
        {
            if (__instance is VehiclePlane plane)
                AircraftFlightPhysics.Remove(plane);
        }
        catch
        {
            // The native object may already be in its destruction path.
        }
    }
}
