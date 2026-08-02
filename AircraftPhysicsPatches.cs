using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

/// <summary>
/// One compact aerodynamic profile. Values describe the aircraft class rather
/// than a second set of player-facing tuning controls.
/// </summary>
internal readonly record struct AircraftFlightProfile(
    string Name,
    float PositiveCriticalAngle,
    float NegativeCriticalAngle,
    float MaximumLiftCoefficient,
    float PositivePeakLiftCoefficient,
    float NegativeMaximumLiftCoefficient,
    float LiftCurveSlope,
    float ZeroLiftAngle,
    float ParasiteDragCoefficient,
    float AspectRatio,
    float OswaldEfficiency,
    float MaximumPitchAcceleration,
    float MaximumYawAcceleration,
    float PitchStability,
    float YawStability,
    float PitchDamping,
    float RollDamping,
    float YawDamping,
    float StaticThrustToWeightAtOne,
    float StaticThrustToWeightAtTen);

internal static class AircraftFlightProfiles
{
    private static readonly AircraftFlightProfile Fighter = new(
        "fighter",
        16f, -14f,
        1.35f, 1.1887f, 1.00f,
        5.2f, -2.5f,
        0.028f, 6.0f, 0.80f,
        112f, 58f,
        0.72f, 0.72f,
        3.6f, 0.8f, 3.2f,
        0.18f, 0.78f);

    private static readonly AircraftFlightProfile Bomber = new(
        "bomber",
        15f, -13f,
        1.45f, 1.1743f, 1.05f,
        4.8f, -2.2f,
        0.035f, 7.5f, 0.82f,
        58f, 31f,
        0.60f, 0.60f,
        4.2f, 1.0f, 4.0f,
        0.15f, 0.65f);

    internal static AircraftFlightProfile For(VehiclePlane plane)
        => plane.planeType == PlaneType.Bomber ? Bomber : Fighter;
}

/// <summary>
/// Runtime telemetry and the small amount of state a physical aircraft needs.
/// Pitch/Roll/Yaw commands use one semantic convention throughout the custom
/// model: +pitch pulls, +roll banks right, +yaw yaws right.
/// </summary>
internal sealed class AircraftFlightState
{
    internal AircraftFlightProfile Profile;

    internal float OriginalMaximumSpeedKmh;
    internal float OriginalClocheMultiplier;
    internal float OriginalThrustResponseSeconds;
    internal float OriginalThrustForceMultiplier;
    internal float OriginalStartLiftMultiplier;
    internal float OriginalEndLiftMultiplier;
    internal float OriginalRigidbodyDrag;
    internal float OriginalRigidbodyAngularDrag;
    internal RigidbodyInterpolation OriginalRigidbodyInterpolation;
    internal bool NativeValuesOverridden;

    internal float BaseStallSpeedMs;
    internal float BaseMaximumSpeedMs;
    internal float StallSpeedMs;
    internal float ReferenceSpeedMs;
    internal float MaximumSpeedMs;
    internal float WingArea;
    internal float WingSpan;
    internal float MeanChord;
    internal float NeutralAngleOfAttack;
    internal float BaselinePropulsivePower;

    internal float CommandPitch;
    internal float CommandRoll;
    internal float CommandYaw;
    internal float EngineSpool;

    internal float AirspeedMs;
    internal float ForwardSpeedMs;
    internal float VerticalSpeedMs;
    internal float AngleOfAttack;
    internal float SideslipAngle;
    internal float DynamicPressure;
    internal float ControlAuthority = 1f;
    internal float PitchAuthority = 1f;
    internal float RollAuthority = 1f;
    internal float YawAuthority = 1f;
    internal float StallSeverity;
    internal bool IsStalled;
    internal bool IsSpinning;
    internal float SpinSeverity;
    internal float SpinDirection;
    internal float AvailableEngineAccelerationMs2;
    internal float AdditionalDragAcceleration;
    internal bool AirborneOwnershipLatched;
    internal Vector3 PreviousTravelVelocity;
    internal bool HasPreviousTravelVelocity;

    internal bool LeftWingLost;
    internal bool RightWingLost;
    internal float TailLoss;
    internal float PropellerLoss;

    internal float TerrainClearance = -1f;
    internal float NextTerrainCheck;
    internal float NextGearTrace;
    internal float NextTelemetry;
    internal bool Faulted;
    internal bool LoggedFailure;
}

internal static class AircraftFlightPhysics
{
    // Recovery baseline: the game's native realistic-flight implementation
    // remains the lift, stall, damage, control-filter, and attitude owner. This
    // layer only bounds engine output and replaces hidden steering speed loss
    // with explicit straight-flight and maneuver drag. The previous full
    // Rigidbody replacement remains dormant until validated aircraft-by-aircraft.
    // Keep the experimental replacement code available for later development,
    // but never let it take runtime ownership in the shipped recovery build.
    // This is readonly instead of const so the compiler still validates both
    // paths without producing unreachable-code warnings.
    private static readonly bool UseReplacementFlightModel = false;
    private const float SeaLevelDensity = 1.225f;
    private const float NativeMaximumThrottle = 100f;
    private const float NoseDownAuthorityFraction = 0.58f;
    private const float AileronIncidenceDegrees = 7.5f;
    private const float MinimumPhysicsSpeed = 0.35f;

    private static readonly Dictionary<int, AircraftFlightState> States = new();

    [ThreadStatic]
    private static VehiclePlane? _nativeFixedUpdateOwner;

    [ThreadStatic]
    private static VehiclePlane? _nativeRecoveryFixedUpdateOwner;

    internal static void Initialize(VehiclePlane plane)
    {
        if (plane == null || !ShouldApply(plane))
            return;

        var id = plane.GetInstanceID();
        if (States.ContainsKey(id))
            return;

        try
        {
            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || rigidbody.mass <= 0.1f)
                return;

            var profile = AircraftFlightProfiles.For(plane);
            var baseMaximumSpeed = ValidPositive(plane.maxKmhSpeed)
                ? Mathf.Clamp(plane.maxKmhSpeed / 3.6f, 42f, 240f)
                : profile.Name == "bomber" ? 112f : 155f;

            var nativeFullLiftSpeed = plane.totalLiftVelocity;
            var baseStallSpeed =
                ValidPositive(nativeFullLiftSpeed) &&
                nativeFullLiftSpeed < baseMaximumSpeed * 0.72f
                    ? nativeFullLiftSpeed * 0.88f
                    : baseMaximumSpeed * (profile.Name == "bomber" ? 0.31f : 0.255f);
            baseStallSpeed = Mathf.Clamp(
                baseStallSpeed,
                18f,
                baseMaximumSpeed * 0.56f);

            var state = new AircraftFlightState
            {
                Profile = profile,
                OriginalMaximumSpeedKmh = plane.maxKmhSpeed,
                OriginalClocheMultiplier = plane.clocheMultiplier,
                OriginalThrustResponseSeconds =
                    plane.timeFromZeroToMaxThrust,
                OriginalThrustForceMultiplier =
                    plane.thrustForceMultiplier,
                OriginalStartLiftMultiplier = plane.startLiftMult,
                OriginalEndLiftMultiplier = plane.endLiftMult,
                OriginalRigidbodyDrag = rigidbody.drag,
                OriginalRigidbodyAngularDrag = rigidbody.angularDrag,
                OriginalRigidbodyInterpolation = rigidbody.interpolation,
                BaseStallSpeedMs = baseStallSpeed,
                BaseMaximumSpeedMs = baseMaximumSpeed,
                NextTelemetry = Time.unscaledTime + UnityEngine.Random.Range(0f, 0.5f)
            };

            RecalculateGeometry(plane, rigidbody, state);
            States[id] = state;

            AiState.Trace(
                $"Aircraft physics: native recovery tuning active for {SafeName(plane)} ({profile.Name}), " +
                $"stall={state.StallSpeedMs * 3.6f:0}km/h " +
                $"maximum={state.MaximumSpeedMs * 3.6f:0}km/h");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Aircraft physics initialization failed: {ex.Message}");
        }
    }

    internal static AircraftNativeFixedUpdateSnapshot BeginNativeFixedUpdate(
        VehiclePlane plane)
    {
        _nativeFixedUpdateOwner = null;
        _nativeRecoveryFixedUpdateOwner = null;
        var originalFallDrag = VehiclePlane.PLANE_FALL_DRAG;

        try
        {
            if (!UseReplacementFlightModel)
            {
                if (!TryGetOwnedState(plane, out var nativeState) ||
                    nativeState.Faulted)
                {
                    return new AircraftNativeFixedUpdateSnapshot(
                        originalFallDrag,
                        false);
                }

                ApplyNativeRecoveryTuning(plane, nativeState);
                _nativeRecoveryFixedUpdateOwner = plane;
                return new AircraftNativeFixedUpdateSnapshot(
                    originalFallDrag,
                    false);
            }

            if (!TryGetOwnedState(plane, out var state) ||
                state.Faulted ||
                !UpdateAirborneOwnership(plane, state))
            {
                return new AircraftNativeFixedUpdateSnapshot(
                    originalFallDrag,
                    false);
            }

            _nativeFixedUpdateOwner = plane;
            VehiclePlane.PLANE_FALL_DRAG = 0f;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody != null)
            {
                // No hidden third damping owner. The forces and moments below own
                // air drag while wheel/ground friction remains native.
                state.NativeValuesOverridden = true;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 0f;
                if (rigidbody.interpolation != RigidbodyInterpolation.Interpolate)
                {
                    rigidbody.interpolation =
                        RigidbodyInterpolation.Interpolate;
                }
            }

            return new AircraftNativeFixedUpdateSnapshot(
                originalFallDrag,
                true);
        }
        catch
        {
            _nativeFixedUpdateOwner = null;
            _nativeRecoveryFixedUpdateOwner = null;
            VehiclePlane.PLANE_FALL_DRAG = originalFallDrag;
            return new AircraftNativeFixedUpdateSnapshot(
                originalFallDrag,
                false);
        }
    }

    internal static void EndNativeFixedUpdate(
        AircraftNativeFixedUpdateSnapshot snapshot)
    {
        _nativeFixedUpdateOwner = null;
        _nativeRecoveryFixedUpdateOwner = null;
        VehiclePlane.PLANE_FALL_DRAG = snapshot.OriginalFallDrag;
    }

    internal static bool SuppressNativeLift(VehiclePlane plane)
        => OwnsCurrentNativeFixedUpdate(plane);

    internal static bool SuppressNativeThrust(VehiclePlane plane)
        => OwnsCurrentNativeFixedUpdate(plane);

    /// <summary>
    /// Native realistic flight rotates the velocity toward the nose every fixed
    /// tick. While the custom model owns flight, keep both its magnitude and its
    /// direction unchanged; only actual forces may change momentum.
    /// </summary>
    internal static void SuppressNativeVelocitySteering(
        Rigidbody rigidbody,
        ref Vector3 requestedVelocity)
    {
        var plane = _nativeFixedUpdateOwner;
        if (plane == null || rigidbody == null)
            return;

        try
        {
            var planeRigidbody = plane.GetRigidbody();
            if (planeRigidbody == null ||
                planeRigidbody.GetInstanceID() != rigidbody.GetInstanceID() ||
                plane.isGrounded ||
                !plane.IsInAerodynamicMode())
            {
                return;
            }

            requestedVelocity = rigidbody.velocity;
        }
        catch
        {
            // If ownership is uncertain, leave the native write alone.
        }
    }

    /// <summary>
    /// The native Realistic model combines a useful velocity-direction update
    /// with a hidden magnitude subtraction. In the shipped recovery model,
    /// preserve that requested direction but let explicit Rigidbody drag,
    /// maneuver drag, gravity, and thrust own speed changes.
    /// </summary>
    internal static void FilterNativeVelocityWrite(
        Rigidbody rigidbody,
        ref Vector3 requestedVelocity)
    {
        var recoveryPlane = _nativeRecoveryFixedUpdateOwner;
        if (recoveryPlane != null && rigidbody != null)
        {
            try
            {
                var planeRigidbody = recoveryPlane.GetRigidbody();
                var eligible =
                    planeRigidbody != null &&
                    planeRigidbody.GetInstanceID() ==
                    rigidbody.GetInstanceID() &&
                    !recoveryPlane.isGrounded &&
                    recoveryPlane.IsInAerodynamicMode();
                if (eligible)
                {
                    var requested = new System.Numerics.Vector3(
                        requestedVelocity.x,
                        requestedVelocity.y,
                        requestedVelocity.z);
                    var currentVelocity = rigidbody.velocity;
                    var current = new System.Numerics.Vector3(
                        currentVelocity.x,
                        currentVelocity.y,
                        currentVelocity.z);
                    var filtered =
                        AircraftAerodynamicsCore
                            .PreserveRequestedVelocityDirection(
                                requested,
                                current,
                                eligible: true);
                    requestedVelocity = new Vector3(
                        filtered.X,
                        filtered.Y,
                        filtered.Z);
                    return;
                }
            }
            catch
            {
                // A despawning or mismatched body keeps the native write.
                return;
            }
        }

        // Retain the complete suppression used only by the dormant replacement
        // model, so it remains internally coherent if development resumes.
        if (rigidbody != null)
            SuppressNativeVelocitySteering(rigidbody, ref requestedVelocity);
    }

    internal static void FixedUpdate(
        VehiclePlane plane,
        bool nativeForcesSuppressedThisTick)
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
            }

            return;
        }

        if (!States.TryGetValue(id, out var state))
        {
            Initialize(plane);
            if (!States.TryGetValue(id, out state))
                return;
        }

        if (state.Faulted)
            return;

        try
        {
            if (!UseReplacementFlightModel)
            {
                UpdateNativeRecoveryState(plane, state);
                return;
            }

            if (!OwnsPhysicalFlight(plane))
            {
                RestoreNativeValues(plane, state);
                return;
            }

            // Never apply both native and custom forces in the same physics
            // step. If ownership was acquired during native FixedUpdate, keep
            // the latch and begin custom physics on the following tick.
            if (!nativeForcesSuppressedThisTick)
                return;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || rigidbody.isKinematic)
                return;

            RecalculateGeometry(plane, rigidbody, state);
            UpdateDamageState(plane, state);
            CaptureNativeSurfaceState(plane);

            state.NativeValuesOverridden = true;
            rigidbody.drag = 0f;
            rigidbody.angularDrag = 0f;
            if (rigidbody.interpolation != RigidbodyInterpolation.Interpolate)
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            var velocity = rigidbody.velocity;
            var localVelocity = plane.transform.InverseTransformDirection(velocity);
            var speed = velocity.magnitude;
            state.AirspeedMs = speed;
            state.ForwardSpeedMs = localVelocity.z;
            state.VerticalSpeedMs = velocity.y;
            state.AngleOfAttack = speed > MinimumPhysicsSpeed
                ? Mathf.Atan2(-localVelocity.y, localVelocity.z) * Mathf.Rad2Deg
                : 0f;
            state.SideslipAngle = speed > MinimumPhysicsSpeed
                ? Mathf.Atan2(
                      localVelocity.x,
                      Mathf.Sqrt(
                          localVelocity.y * localVelocity.y +
                          localVelocity.z * localVelocity.z)) *
                  Mathf.Rad2Deg
                : 0f;

            var density = AirDensity(plane);
            state.DynamicPressure = 0.5f * density * speed * speed;

            UpdateEngineAndApplyThrust(plane, rigidbody, state);
            ApplyAerodynamicForces(plane, rigidbody, state, density);
            ApplyStabilityAndControlMoments(plane, rigidbody, state, density);
            WriteTelemetry(plane, state);
        }
        catch (Exception ex)
        {
            state.Faulted = true;
            if (!state.LoggedFailure)
            {
                state.LoggedFailure = true;
                Plugin.LogSource.LogWarning(
                    $"Aircraft physics returned {SafeName(plane)} to native flight after a failure: {ex.Message}");
            }

            RestoreNativeValues(plane, state);
        }
    }

    /// <summary>
    /// Keeps the native flight model intact and adjusts only the few direct,
    /// understandable aircraft controls exposed in the settings menu.
    /// </summary>
    private static void ApplyNativeRecoveryTuning(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        var speedScale = Mathf.Clamp(
            Settings.AircraftWorldSpeedScale.Value,
            0.65f,
            1.35f);
        var inverseSpeedSquared =
            1f / Mathf.Max(0.25f, speedScale * speedScale);
        var engineParameters = new AircraftEngineParameters(
            state.Profile.StaticThrustToWeightAtOne,
            state.Profile.StaticThrustToWeightAtTen,
            45f,
            0.24f,
            0.44f);
        var rating = Mathf.Clamp(
            Settings.AircraftEnginePowerMultiplier.Value,
            1f,
            10f);
        var ratingThrust =
            AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
                rating,
                engineParameters);
        var baselineThrust =
            AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
                4f,
                engineParameters);
        var engineScale =
            ratingThrust / Mathf.Max(0.01f, baselineThrust);

        state.NativeValuesOverridden = true;
        state.StallSpeedMs = state.BaseStallSpeedMs * speedScale;
        state.MaximumSpeedMs = state.BaseMaximumSpeedMs * speedScale;
        state.ReferenceSpeedMs = Mathf.Max(
            state.StallSpeedMs * 1.85f,
            state.MaximumSpeedMs * 0.58f);

        plane.maxKmhSpeed =
            state.OriginalMaximumSpeedKmh * speedScale;
        plane.startLiftMult =
            state.OriginalStartLiftMultiplier * inverseSpeedSquared;
        plane.endLiftMult =
            state.OriginalEndLiftMultiplier * inverseSpeedSquared;
        var rigidbody = plane.GetRigidbody();
        if (rigidbody != null)
        {
            var authoredThrustMultiplier =
                state.OriginalThrustForceMultiplier *
                speedScale *
                speedScale *
                engineScale;
            var axialSpeed = Mathf.Max(
                0f,
                Vector3.Dot(
                    rigidbody.velocity,
                    plane.transform.forward));
            var densityMultiplier = AirDensityMultiplier(plane);
            var appliedThrustMultiplier =
                AircraftAerodynamicsCore.NativeThrustForceMultiplier(
                    authoredThrustMultiplier,
                    NativeMaximumThrottle,
                    rigidbody.mass,
                    rating,
                    axialSpeed,
                    densityMultiplier,
                    45f,
                    engineParameters);
            plane.thrustForceMultiplier = appliedThrustMultiplier;

            // A power-rating change must not leave one stale native thrust
            // sample above the newly selected physical envelope.
            var maximumNativeThrust =
                appliedThrustMultiplier * NativeMaximumThrottle;
            if (float.IsFinite(plane.thrustForce))
            {
                plane.thrustForce = Mathf.Clamp(
                    plane.thrustForce,
                    0f,
                    maximumNativeThrust);
            }

            state.AvailableEngineAccelerationMs2 =
                Mathf.Max(0f, plane.thrustForce) *
                densityMultiplier /
                Mathf.Max(0.1f, rigidbody.mass);
            rigidbody.drag = plane.isGrounded
                ? state.OriginalRigidbodyDrag
                : AircraftAerodynamicsCore
                    .MapAerodynamicDragToRigidbodyDrag(
                        state.OriginalRigidbodyDrag,
                        Settings.AircraftAerodynamicDragMultiplier.Value);
        }
        else
        {
            plane.thrustForceMultiplier =
                state.OriginalThrustForceMultiplier;
            state.AvailableEngineAccelerationMs2 = 0f;
        }

        // Native spool-up/spool-down, damage, stalls, lift, velocity direction,
        // and attitude remain authoritative. Throttle is treated as power rather
        // than a target speed by the narrowly guarded governor patch below.
        // Straight-flight damping stays low; the postfix adds only bounded
        // drag from actual flight-path curvature.
        plane.timeFromZeroToMaxThrust =
            state.OriginalThrustResponseSeconds;
        plane.clocheMultiplier = state.OriginalClocheMultiplier;
    }

    private static void UpdateNativeRecoveryState(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        var rigidbody = plane.GetRigidbody();
        if (rigidbody == null || rigidbody.isKinematic)
            return;

        var velocity = rigidbody.velocity;
        var localVelocity =
            plane.transform.InverseTransformDirection(velocity);
        var speed = velocity.magnitude;
        state.AirspeedMs = speed;
        state.ForwardSpeedMs = localVelocity.z;
        state.VerticalSpeedMs = velocity.y;
        state.AngleOfAttack = speed > MinimumPhysicsSpeed
            ? Mathf.Atan2(-localVelocity.y, localVelocity.z) *
              Mathf.Rad2Deg
            : 0f;
        state.SideslipAngle = speed > MinimumPhysicsSpeed
            ? Mathf.Atan2(
                  localVelocity.x,
                  Mathf.Sqrt(
                      localVelocity.y * localVelocity.y +
                      localVelocity.z * localVelocity.z)) *
              Mathf.Rad2Deg
            : 0f;
        state.DynamicPressure =
            0.5f * AirDensity(plane) * speed * speed;
        state.ControlAuthority = 1f;
        state.PitchAuthority = 1f;
        state.RollAuthority = 1f;
        state.YawAuthority = 1f;
        state.StallSeverity = 0f;
        state.IsStalled =
            !plane.isGrounded && !plane.IsInAerodynamicMode();
        state.IsSpinning = false;
        state.SpinSeverity = 0f;
        state.EngineSpool =
            Mathf.Clamp01(plane.throttle / NativeMaximumThrottle);
        var maneuverDragAcceleration = 0f;
        if (!plane.isGrounded &&
            speed > MinimumPhysicsSpeed &&
            FiniteVector(velocity))
        {
            var currentTravelVelocity =
                new System.Numerics.Vector3(
                    velocity.x,
                    velocity.y,
                    velocity.z);
            var flightPathTurnRate =
                state.HasPreviousTravelVelocity
                    ? AircraftAerodynamicsCore
                        .FlightPathTurnRateRadiansPerSecond(
                            new System.Numerics.Vector3(
                                state.PreviousTravelVelocity.x,
                                state.PreviousTravelVelocity.y,
                                state.PreviousTravelVelocity.z),
                            currentTravelVelocity,
                            Mathf.Max(0.001f, Time.fixedDeltaTime))
                    : 0f;
            maneuverDragAcceleration =
                AircraftAerodynamicsCore.NativeManeuverDragAcceleration(
                    rigidbody.mass,
                    flightPathTurnRate,
                    AirDensity(plane),
                    state.WingArea,
                    state.Profile.AspectRatio,
                    state.Profile.OswaldEfficiency,
                    speed,
                    state.StallSpeedMs,
                    Settings.AircraftAerodynamicDragMultiplier.Value);
            if (maneuverDragAcceleration > 0f)
            {
                rigidbody.AddForce(
                    -velocity.normalized * maneuverDragAcceleration,
                    ForceMode.Acceleration);
            }

            state.PreviousTravelVelocity = velocity;
            state.HasPreviousTravelVelocity = true;
        }
        else
        {
            state.PreviousTravelVelocity = Vector3.zero;
            state.HasPreviousTravelVelocity = false;
        }

        state.AdditionalDragAcceleration =
            maneuverDragAcceleration;

        WriteTelemetry(plane, state);
    }

    internal static bool TryGetState(
        VehiclePlane plane,
        out AircraftFlightState state)
    {
        state = null!;
        if (plane == null || !ShouldApply(plane))
            return false;

        if (!States.TryGetValue(plane.GetInstanceID(), out var existing))
        {
            Initialize(plane);
            if (!States.TryGetValue(plane.GetInstanceID(), out existing))
                return false;
        }

        if (existing.Faulted)
            return false;

        state = existing;
        return true;
    }

    /// <summary>
    /// The native realistic input helpers remain the sole input filters. We read
    /// their final normalized virtual-surface values and convert signs once.
    /// </summary>
    internal static void CaptureNativeSurfaceState(VehiclePlane plane)
    {
        if (!TryGetOwnedState(plane, out var state) ||
            state.Faulted ||
            (UseReplacementFlightModel &&
             !state.AirborneOwnershipLatched) ||
            !plane.HasDriverAlive)
            return;

        state.CommandYaw = FiniteClamped(plane.r_yaw);
        state.CommandPitch = FiniteClamped(-plane.r_pitch);
        state.CommandRoll = FiniteClamped(plane.r_roll);
    }

    internal static AircraftNativeUpdateSnapshot BeginNativeUpdate(
        VehiclePlane plane)
    {
        var originalYaw = 0f;
        var originalPitch = 0f;
        var originalRoll = 0f;
        var capturedOriginal = false;
        try
        {
            originalYaw = plane.r_yaw;
            originalPitch = plane.r_pitch;
            originalRoll = plane.r_roll;
            capturedOriginal = true;
            if (UseReplacementFlightModel ||
                !TryGetOwnedState(plane, out var state) ||
                state.Faulted)
            {
                return default;
            }

            if (!float.IsFinite(originalYaw) ||
                !float.IsFinite(originalPitch) ||
                !float.IsFinite(originalRoll))
            {
                return default;
            }

            var airspeedMs = state.AirspeedMs;
            var rigidbody = plane.GetRigidbody();
            if (rigidbody != null && FiniteVector(rigidbody.velocity))
                airspeedMs = rigidbody.velocity.magnitude;
            var schedule =
                AircraftAerodynamicsCore.EvaluateNativeControlSchedule(
                    airspeedMs,
                    state.ReferenceSpeedMs);

            plane.r_yaw =
                AircraftAerodynamicsCore.ScaleNativeYawCommand(
                    AircraftAerodynamicsCore.LimitNativeControlTravel(
                        originalYaw,
                        schedule.Rudder),
                    Settings.AircraftRudderAuthorityMultiplier.Value);
            plane.r_pitch =
                AircraftAerodynamicsCore.ScaleNativePitchCommand(
                    originalPitch,
                    Settings.AircraftPitchAuthorityMultiplier.Value);
            plane.r_roll =
                AircraftAerodynamicsCore.ScaleNativeRollCommand(
                    originalRoll,
                    plane.planeType == PlaneType.Bomber,
                    Settings.AircraftRollAuthorityMultiplier.Value);
            return new AircraftNativeUpdateSnapshot(
                originalYaw,
                originalPitch,
                originalRoll,
                Overridden: true);
        }
        catch
        {
            if (capturedOriginal)
            {
                try
                {
                    plane.r_yaw = originalYaw;
                    plane.r_pitch = originalPitch;
                    plane.r_roll = originalRoll;
                }
                catch
                {
                    // Preserve the native failure path for a despawning aircraft.
                }
            }

            return default;
        }
    }

    internal static void EndNativeUpdate(
        VehiclePlane plane,
        AircraftNativeUpdateSnapshot snapshot)
    {
        if (!snapshot.Overridden || plane == null)
            return;

        try
        {
            plane.r_yaw = snapshot.OriginalYaw;
            plane.r_pitch = snapshot.OriginalPitch;
            plane.r_roll = snapshot.OriginalRoll;
        }
        catch
        {
            // The native object may have despawned during Update.
        }
    }

    internal static void DecoupleThrottleSpeedGovernor(
        VehiclePlane plane,
        ref bool canApplyThrust)
    {
        if (canApplyThrust ||
            plane == null ||
            !Settings.AircraftThrottleControlsEnginePower.Value ||
            !Settings.AircraftFlightPhysicsEnabled.Value)
        {
            return;
        }

        try
        {
            if (!ShouldApply(plane) ||
                plane.hasMissingParts ||
                !plane.engineStarted ||
                plane.isGrounded ||
                !plane.IsInAerodynamicMode())
            {
                return;
            }

            var normalizedThrottle =
                Mathf.Clamp01(plane.throttle / NativeMaximumThrottle);
            if (normalizedThrottle <= 0.001f)
                return;

            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null)
                return;

            var forwardSpeed =
                plane.transform
                    .InverseTransformDirection(rigidbody.velocity)
                    .z;
            var nativeThrottleSpeed =
                Mathf.Max(0f, plane.maxKmhSpeed / 3.6f) *
                normalizedThrottle;

            // A false argument at this exact threshold is the native
            // throttle-proportional speed governor. Other false paths remain
            // untouched so damage and engine shutdown stay authoritative.
            if (forwardSpeed >= nativeThrottleSpeed - 0.5f)
                canApplyThrust = true;
        }
        catch
        {
            // Preserve the native decision if state changes during the call.
        }
    }

    internal static bool AllowLandingGearExtension(VehiclePlane plane)
    {
        if (plane == null || !ShouldApply(plane) || plane.isGrounded)
            return true;

        try
        {
            var rigidbody = plane.GetRigidbody();
            if (rigidbody == null || !TryGetState(plane, out var state))
                return true;

            var extensionLimit =
                AircraftAerodynamicsCore.LandingGearExtensionSpeedLimit(
                    state.MaximumSpeedMs,
                    state.StallSpeedMs,
                    state.Profile.Name == "bomber");
            var airspeed = rigidbody.velocity.magnitude;
            if (AircraftAerodynamicsCore.LandingGearExtensionAllowed(
                    ownsAircraftFlight: true,
                    plane.isGrounded,
                    airspeed,
                    extensionLimit))
            {
                return true;
            }

            if (Time.unscaledTime >= state.NextGearTrace)
            {
                state.NextGearTrace = Time.unscaledTime + 2f;
                AiState.Trace(
                    $"Landing gear interlock: {SafeName(plane)} blocked at " +
                    $"{airspeed * 3.6f:0}km/h " +
                    $"(limit {extensionLimit * 3.6f:0}km/h)");
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    internal static float TerrainClearanceAhead(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        var now = Time.unscaledTime;
        if (now < state.NextTerrainCheck)
            return state.TerrainClearance;

        state.NextTerrainCheck = now + 0.25f;
        var flatForward = Vector3.ProjectOnPlane(
            plane.transform.forward,
            Vector3.up);
        flatForward = flatForward.sqrMagnitude > 0.01f
            ? flatForward.normalized
            : Vector3.forward;
        var origin =
            plane.transform.position + flatForward * 30f + Vector3.up * 4f;
        state.TerrainClearance = Physics.Raycast(
            origin,
            Vector3.down,
            out var hit,
            1000f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore)
            ? Mathf.Max(0f, hit.distance - 4f)
            : -1f;
        return state.TerrainClearance;
    }

    internal static void Remove(VehiclePlane plane)
    {
        if (plane == null)
            return;

        var id = plane.GetInstanceID();
        if (!States.TryGetValue(id, out var state))
            return;

        RestoreNativeValues(plane, state);
        States.Remove(id);
    }

    internal static bool ShouldApply(VehiclePlane plane)
        => Settings.AircraftFlightPhysicsEnabled.Value &&
           (IsLocallyControlledHumanRealisticPlane(plane) ||
            IsAuthoritativeAiPlane(plane));

    private static bool IsAuthoritativeAiPlane(VehiclePlane plane)
    {
        if (plane == null)
            return false;

        try
        {
            var driver = plane.GetDriver();
            var multiplayerIntent =
                MatchData.data != null &&
                MatchData.data.isMultiplayer;
            return AircraftFlightOwnershipCore.CanSimulateAi(
                enabled: true,
                Settings.AircraftAiFlightModelExperimentalEnabled.Value,
                driver != null && AiOwnership.IsAiControlled(driver),
                multiplayerIntent,
                Photon.Pun.PhotonNetwork.InRoom,
                Photon.Pun.PhotonNetwork.IsMasterClient);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsLocallyControlledHumanRealisticPlane(
        VehiclePlane plane)
    {
        if (plane == null)
            return false;

        try
        {
            var controller = PlayerController.currentController;
            var controlledVehicle = controller?.ControlledVehicle;
            var isLocallyControlledVehicle =
                controlledVehicle != null &&
                controlledVehicle.GetInstanceID() == plane.GetInstanceID();
            var driver = plane.GetDriver();
            var hasHumanDriver =
                driver != null &&
                !AiOwnership.IsAiControlled(driver);
            var multiplayerIntent =
                MatchData.data != null &&
                MatchData.data.isMultiplayer;
            var inNetworkRoom = Photon.Pun.PhotonNetwork.InRoom;
            var ownsNetworkSynchronizer = false;
            if (inNetworkRoom)
            {
                var synchronizer = plane.GetSyncher();
                ownsNetworkSynchronizer =
                    synchronizer != null &&
                    (synchronizer.IsMine ||
                     synchronizer.OwnSyncherOnline);
            }

            return AircraftFlightOwnershipCore.CanSimulate(
                true,
                isLocallyControlledVehicle,
                hasHumanDriver,
                plane.PlayerIsDrivingWithRealisticControls(),
                multiplayerIntent,
                inNetworkRoom,
                ownsNetworkSynchronizer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The native aerodynamic-mode flag falls back to false when a stalled
    /// aircraft becomes very slow. Latch ownership after takeoff so a real
    /// zero-speed stall remains in the custom model instead of switching flight
    /// models at the worst possible moment. Grounding, driver loss, or native
    /// structural inactivity release ownership immediately.
    /// </summary>
    internal static bool OwnsPhysicalFlight(VehiclePlane plane)
    {
        if (!UseReplacementFlightModel)
            return false;

        if (!TryGetOwnedState(plane, out var state) || state.Faulted)
            return false;

        try
        {
            return state.AirborneOwnershipLatched &&
                   plane.HasDriverAlive;
        }
        catch
        {
            return false;
        }
    }

    private static bool UpdateAirborneOwnership(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        try
        {
            if (!ShouldApply(plane) ||
                !plane.HasDriverAlive)
            {
                state.AirborneOwnershipLatched = false;
                return false;
            }

            if (state.AirborneOwnershipLatched)
            {
                // A high-speed wheel or terrain contact can flicker the native
                // grounded flag for one tick. Keep one physics owner while the
                // aircraft is still in aerodynamic mode, but hand control back
                // after an actual landing.
                if (plane.isGrounded &&
                    !plane.IsInAerodynamicMode())
                {
                    state.AirborneOwnershipLatched = false;
                }

                return state.AirborneOwnershipLatched;
            }

            // IsActive flickers false at the top of a deep stall. It is a
            // valid acquisition guard, but not a valid reason to hand an
            // already airborne aircraft back to the native model.
            if (plane.isGrounded ||
                !plane.IsActive())
                return false;

            state.AirborneOwnershipLatched =
                plane.IsInAerodynamicMode();
            return state.AirborneOwnershipLatched;
        }
        catch
        {
            state.AirborneOwnershipLatched = false;
            return false;
        }
    }

    private static void RecalculateGeometry(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state)
    {
        // Only the understandable speed slider owns envelope scaling. Legacy
        // fighter/bomber multipliers deliberately do not enter this calculation.
        var speedScale = Mathf.Clamp(
            Settings.AircraftWorldSpeedScale.Value,
            0.65f,
            1.35f);
        state.StallSpeedMs = state.BaseStallSpeedMs * speedScale;
        state.MaximumSpeedMs = state.BaseMaximumSpeedMs * speedScale;
        state.ReferenceSpeedMs = Mathf.Max(
            state.StallSpeedMs * 1.85f,
            state.MaximumSpeedMs * 0.58f);
        if (state.AirborneOwnershipLatched)
        {
            state.NativeValuesOverridden = true;
            plane.maxKmhSpeed =
                state.OriginalMaximumSpeedKmh * speedScale;
        }

        // Geometry is an aircraft property. Calibrating it from the live
        // atmospheric density would silently enlarge the wing as the aircraft
        // climbs and cancel the intended altitude effect.
        var density = SeaLevelDensity;
        var weight = rigidbody.mass * Physics.gravity.magnitude;
        var usableMaximumLift =
            state.Profile.PositivePeakLiftCoefficient;
        state.WingArea =
            2f * weight /
            Mathf.Max(
                1f,
                density *
                state.StallSpeedMs *
                state.StallSpeedMs *
                usableMaximumLift);
        state.WingArea = Mathf.Clamp(state.WingArea, 4f, 220f);
        state.WingSpan = Mathf.Sqrt(
            state.Profile.AspectRatio * state.WingArea);
        state.MeanChord = state.WingArea / Mathf.Max(1f, state.WingSpan);

        // The neutral pitch attitude is derived from the lift actually required
        // at the reference cruise speed. This removes the old hard-coded
        // several-degree nose-up bias while still requiring the pilot to pull as
        // the aircraft slows.
        var referencePressure =
            0.5f *
            density *
            state.ReferenceSpeedMs *
            state.ReferenceSpeedMs;
        var requiredLiftCoefficient =
            weight /
            Mathf.Max(1f, referencePressure * state.WingArea);
        var normalizedLift = Mathf.Clamp(
            requiredLiftCoefficient /
            Mathf.Max(0.01f, state.Profile.MaximumLiftCoefficient),
            -0.95f,
            0.95f);
        var inverseTanh =
            0.5f *
            Mathf.Log(
                (1f + normalizedLift) /
                Mathf.Max(0.001f, 1f - normalizedLift));
        var effectiveTrimRadians =
            inverseTanh *
            state.Profile.MaximumLiftCoefficient /
            Mathf.Max(0.01f, state.Profile.LiftCurveSlope);
        state.NeutralAngleOfAttack =
            state.Profile.ZeroLiftAngle +
            effectiveTrimRadians * Mathf.Rad2Deg;

        // Rating four is the baseline design engine: at maximum level speed,
        // available propulsive power approximately equals clean level drag.
        var qMaximum =
            0.5f * density * state.MaximumSpeedMs * state.MaximumSpeedMs;
        var levelCl = weight / Mathf.Max(1f, qMaximum * state.WingArea);
        var induced =
            levelCl * levelCl /
            (Mathf.PI *
             state.Profile.AspectRatio *
             state.Profile.OswaldEfficiency);
        var baselineCd =
            state.Profile.ParasiteDragCoefficient + induced;
        var designDrag = qMaximum * state.WingArea * baselineCd;
        state.BaselinePropulsivePower =
            Mathf.Max(1000f, designDrag * state.MaximumSpeedMs);
    }

    private static void UpdateEngineAndApplyThrust(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state)
    {
        var propellerRemaining = Settings.AircraftDamagePhysicsEnabled.Value
            ? Mathf.Clamp01(1f - state.PropellerLoss)
            : 1f;
        var throttle = propellerRemaining > 0.001f
            ? Mathf.Clamp01(plane.throttle / NativeMaximumThrottle)
            : 0f;
        var engineParameters = new AircraftEngineParameters(
            state.Profile.StaticThrustToWeightAtOne,
            state.Profile.StaticThrustToWeightAtTen,
            45f,
            0.24f,
            0.44f);
        state.EngineSpool = AircraftAerodynamicsCore.AdvanceEngineSpool(
            state.EngineSpool,
            throttle,
            Mathf.Max(0.001f, Time.fixedDeltaTime),
            engineParameters);

        var rating = Mathf.Clamp(
            Settings.AircraftEnginePowerMultiplier.Value,
            1f,
            10f);
        var weight = rigidbody.mass * Physics.gravity.magnitude;
        var staticAtFour =
            AircraftAerodynamicsCore.MapEngineRatingToStaticThrustToWeight(
                4f,
                engineParameters);
        var powerTransitionSpeed =
            state.BaselinePropulsivePower /
            Mathf.Max(1f, weight * staticAtFour);
        engineParameters = engineParameters with
        {
            PowerTransitionSpeed = Mathf.Max(8f, powerTransitionSpeed)
        };
        var axialSpeed = Mathf.Max(
            0f,
            Vector3.Dot(rigidbody.velocity, plane.transform.forward));
        var thrust = AircraftAerodynamicsCore.PowerLimitedThrust(
            rigidbody.mass,
            rating,
            state.EngineSpool * propellerRemaining,
            axialSpeed,
            engineParameters);
        if (!float.IsFinite(thrust) || thrust < 0f)
            thrust = 0f;

        plane._trgtThrust = thrust;
        plane.thrustForce = thrust;
        state.AvailableEngineAccelerationMs2 =
            thrust / Mathf.Max(0.1f, rigidbody.mass);

        if (thrust > 0.001f)
        {
            rigidbody.AddForce(
                plane.transform.forward * thrust,
                ForceMode.Force);
        }
    }

    private static void ApplyAerodynamicForces(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        float density)
    {
        if (state.AirspeedMs < MinimumPhysicsSpeed)
        {
            state.StallSeverity = Approach(
                state.StallSeverity,
                0f,
                2f,
                Time.fixedDeltaTime);
            state.IsStalled = false;
            state.IsSpinning = false;
            state.SpinSeverity = 0f;
            state.AdditionalDragAcceleration = 0f;
            return;
        }

        var rollCommand = Mathf.Clamp(
            state.CommandRoll *
            Settings.AircraftRollAuthorityMultiplier.Value,
            -2f,
            2f);

        var highSpeedHeaviness = HighSpeedControlHeaviness(state);
        var incidence =
            rollCommand *
            highSpeedHeaviness *
            AileronIncidenceDegrees;

        var center = rigidbody.worldCenterOfMass;
        var halfSpanOffset =
            plane.transform.right * (state.WingSpan * 0.25f);
        var leftPosition = center - halfSpanOffset;
        var rightPosition = center + halfSpanOffset;

        var leftAreaFraction = state.LeftWingLost ? 0.035f : 1f;
        var rightAreaFraction = state.RightWingLost ? 0.035f : 1f;
        var leftResult = ApplyWingPanel(
            plane,
            rigidbody,
            state,
            density,
            leftPosition,
            state.WingArea * 0.5f * leftAreaFraction,
            incidence);
        var rightResult = ApplyWingPanel(
            plane,
            rigidbody,
            state,
            density,
            rightPosition,
            state.WingArea * 0.5f * rightAreaFraction,
            -incidence);

        // A detached wing leaves a blunt stump. Its force still vanishes with
        // dynamic pressure; no arbitrary minimum roll torque is injected.
        if (state.LeftWingLost)
            ApplyWingStumpDrag(plane, rigidbody, state, density, leftPosition);
        if (state.RightWingLost)
            ApplyWingStumpDrag(plane, rigidbody, state, density, rightPosition);

        var betaRadians = state.SideslipAngle * Mathf.Deg2Rad;
        var sideCoefficient = -0.82f * betaRadians;
        var sideForce =
            plane.transform.right *
            (state.DynamicPressure *
             state.WingArea *
             sideCoefficient);
        if (FiniteVector(sideForce))
            rigidbody.AddForce(sideForce, ForceMode.Force);

        var averageSeparation =
            (leftResult.Separation + rightResult.Separation) * 0.5f;
        var targetStall = Settings.AircraftStallPhysicsEnabled.Value
            ? averageSeparation
            : 0f;
        state.StallSeverity = Approach(
            state.StallSeverity,
            targetStall,
            targetStall > state.StallSeverity ? 4.5f : 2.4f,
            Time.fixedDeltaTime);
        state.IsStalled = state.IsStalled
            ? state.StallSeverity > 0.12f
            : state.StallSeverity > 0.28f;

        var asymmetry = rightResult.Separation - leftResult.Separation;
        var localAngular =
            plane.transform.InverseTransformDirection(
                rigidbody.angularVelocity);
        state.SpinSeverity = Mathf.Clamp01(
            state.StallSeverity *
            Mathf.Max(
                Mathf.Abs(asymmetry) * 1.8f,
                Mathf.InverseLerp(
                    12f,
                    55f,
                    Mathf.Abs(localAngular.y) * Mathf.Rad2Deg +
                    Mathf.Abs(localAngular.z) * Mathf.Rad2Deg)));
        state.IsSpinning =
            state.SpinSeverity > 0.35f &&
            state.StallSeverity > 0.45f;
        state.SpinDirection = Mathf.Abs(asymmetry) > 0.01f
            ? Mathf.Sign(asymmetry)
            : Mathf.Sign(localAngular.y - localAngular.z);

        var totalDrag = leftResult.Drag + rightResult.Drag;
        state.AdditionalDragAcceleration =
            totalDrag / Mathf.Max(0.1f, rigidbody.mass);
    }

    private static WingPanelResult ApplyWingPanel(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        float density,
        Vector3 position,
        float area,
        float incidenceDegrees)
    {
        if (area <= 0.001f)
            return default;

        var pointVelocity =
            rigidbody.velocity +
            Vector3.Cross(
                rigidbody.angularVelocity,
                position - rigidbody.worldCenterOfMass);
        // A finite wing section produces lift and profile drag from chordwise
        // flow. Spanwise flow is handled by the separate sideslip force below.
        var spanDirection = plane.transform.right;
        var sectionVelocity =
            pointVelocity -
            spanDirection *
            Vector3.Dot(pointVelocity, spanDirection);
        var speed = sectionVelocity.magnitude;
        if (speed < MinimumPhysicsSpeed)
            return default;

        var localVelocity =
            plane.transform.InverseTransformDirection(sectionVelocity);
        var alpha =
            Mathf.Atan2(-localVelocity.y, localVelocity.z) *
            Mathf.Rad2Deg +
            incidenceDegrees;
        // The positive/negative profile changes at zero lift, not at zero
        // geometric incidence. Branching on geometric zero introduced a
        // discontinuity while the wing was still making positive lift.
        var effectiveAlpha = Mathf.DeltaAngle(
            state.Profile.ZeroLiftAngle,
            alpha);
        var positiveAngle = effectiveAlpha >= 0f;
        var maximumLift = positiveAngle
            ? state.Profile.MaximumLiftCoefficient
            : state.Profile.NegativeMaximumLiftCoefficient;
        var criticalAngle = positiveAngle
            ? state.Profile.PositiveCriticalAngle
            : Mathf.Abs(state.Profile.NegativeCriticalAngle);
        var inducedFactor =
            1f /
            (Mathf.PI *
             state.Profile.AspectRatio *
             state.Profile.OswaldEfficiency);
        var stallEnabled = Settings.AircraftStallPhysicsEnabled.Value;
        var aerodynamicParameters = new AircraftAerodynamicsParameters(
            density,
            state.WingArea,
            state.WingSpan,
            state.MeanChord,
            state.Profile.ZeroLiftAngle,
            state.Profile.LiftCurveSlope,
            maximumLift,
            stallEnabled ? criticalAngle : 179f,
            stallEnabled ? 9f : 1f,
            0.90f,
            state.Profile.ParasiteDragCoefficient,
            inducedFactor,
            stallEnabled ? 0.72f : 0f,
            AileronIncidenceDegrees,
            state.ReferenceSpeedMs,
            0.30f,
            0.42f,
            0.70f,
            0.16f,
            NoseDownAuthorityFraction);
        var liftState = AircraftAerodynamicsCore.EvaluateLift(
            alpha,
            aerodynamicParameters);
        var separation = stallEnabled ? liftState.Separation : 0f;
        var cl = liftState.LiftCoefficient;
        var baseCd = AircraftAerodynamicsCore.EvaluateDragCoefficient(
            cl,
            separation,
            aerodynamicParameters);
        var dragScale = Mathf.Clamp(
            Settings.AircraftAerodynamicDragMultiplier.Value,
            0.60f,
            1.60f);
        var cd = baseCd;
        if (plane.gear_opened)
        {
            cd += state.Profile.Name == "bomber" ? 0.060f : 0.045f;
        }

        var overspeed = Mathf.Max(
            0f,
            speed / Mathf.Max(1f, state.MaximumSpeedMs * 1.18f) - 1f);
        cd += overspeed * overspeed * 1.25f;
        cd *= dragScale;

        var q = 0.5f * density * speed * speed;
        var velocityDirection = sectionVelocity / speed;
        var liftDirection = Vector3.Cross(
            velocityDirection,
            spanDirection);
        if (liftDirection.sqrMagnitude < 0.000001f)
            return default;
        liftDirection.Normalize();

        var lift = q * area * cl;
        var drag = q * area * Mathf.Max(0f, cd);
        var force =
            liftDirection * lift -
            velocityDirection * drag;
        if (FiniteVector(force))
            rigidbody.AddForceAtPosition(force, position, ForceMode.Force);

        return new WingPanelResult(separation, Mathf.Max(0f, drag));
    }

    private static void ApplyWingStumpDrag(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        float density,
        Vector3 position)
    {
        var velocity =
            rigidbody.velocity +
            Vector3.Cross(
                rigidbody.angularVelocity,
                position - rigidbody.worldCenterOfMass);
        var speed = velocity.magnitude;
        if (speed < MinimumPhysicsSpeed)
            return;

        var q = 0.5f * density * speed * speed;
        var dragScale = Mathf.Clamp(
            Settings.AircraftAerodynamicDragMultiplier.Value,
            0.60f,
            1.60f);
        var force =
            -velocity.normalized *
            (q *
             state.WingArea *
             0.5f *
             0.18f *
             dragScale);
        if (FiniteVector(force))
            rigidbody.AddForceAtPosition(force, position, ForceMode.Force);
    }

    private static void ApplyStabilityAndControlMoments(
        VehiclePlane plane,
        Rigidbody rigidbody,
        AircraftFlightState state,
        float density)
    {
        var qAtStall =
            0.5f *
            density *
            state.StallSpeedMs *
            state.StallSpeedMs;
        var qRatio = state.DynamicPressure / Mathf.Max(1f, qAtStall);
        var highSpeedHeaviness = HighSpeedControlHeaviness(state);
        var propwash =
            state.EngineSpool *
            Mathf.Clamp01(1f - state.PropellerLoss);

        var wingAuthority =
            Mathf.Clamp01(qRatio / 1.50f) *
            highSpeedHeaviness;
        var tailFreestream =
            Mathf.Clamp01(qRatio / 1.12f) *
            highSpeedHeaviness;
        // Propwash retains a limited amount of tail authority at low airspeed,
        // but it cannot simply add to freestream authority and saturate the
        // controls. Elevator and rudder also have different immersed areas.
        var elevatorAuthority =
            1f -
            (1f - tailFreestream) *
            (1f - 0.30f * propwash);
        var rudderAuthority =
            1f -
            (1f - tailFreestream) *
            (1f - 0.42f * propwash);
        var tailEffectiveness =
            1f - state.TailLoss * 0.78f;

        var separationLoss =
            1f - state.StallSeverity * 0.58f;
        wingAuthority *= separationLoss;
        var separatedTailAuthority =
            Mathf.Lerp(1f, 0.64f, state.StallSeverity);
        elevatorAuthority *= separatedTailAuthority;
        rudderAuthority *= separatedTailAuthority;
        if (state.LeftWingLost || state.RightWingLost)
            wingAuthority *= 0.58f;
        elevatorAuthority *= tailEffectiveness;
        rudderAuthority *= tailEffectiveness;
        var tailStabilityAuthority =
            tailFreestream *
            tailEffectiveness *
            separatedTailAuthority;

        state.ControlAuthority = Mathf.Min(
            wingAuthority,
            Mathf.Min(elevatorAuthority, rudderAuthority));
        state.PitchAuthority = elevatorAuthority;
        state.RollAuthority = wingAuthority;
        state.YawAuthority = rudderAuthority;

        var pitchCommand = Mathf.Clamp(
            state.CommandPitch *
            Settings.AircraftPitchAuthorityMultiplier.Value,
            -2f,
            2f);
        if (pitchCommand < 0f)
            pitchCommand *= NoseDownAuthorityFraction;
        var yawCommand = Mathf.Clamp(
            state.CommandYaw *
            Settings.AircraftRudderAuthorityMultiplier.Value,
            -3.75f,
            3.75f);

        var localAngular =
            plane.transform.InverseTransformDirection(
                rigidbody.angularVelocity);

        // Unity local +X is nose-down, +Y is right yaw, and +Z is left
        // bank. Convert semantic commands at this single boundary.
        var pitchAcceleration =
            -pitchCommand *
            state.Profile.MaximumPitchAcceleration *
            elevatorAuthority;
        // Aileron input already changes the two wing incidences and therefore
        // creates its roll moment at the wing panels. Do not add a second direct
        // aileron torque here; roll damping is the only body-axis roll moment.
        var rollAcceleration = 0f;
        var yawAcceleration =
            yawCommand *
            state.Profile.MaximumYawAcceleration *
            rudderAuthority;

        // Static stability is relative to airflow, never to the horizon.
        // Existing rates receive aerodynamic damping without a target attitude.
        var boundedPitchStabilityError =
            AircraftAerodynamicsCore.BoundedStabilityErrorDegrees(
                state.AngleOfAttack,
                state.NeutralAngleOfAttack);
        pitchAcceleration +=
            boundedPitchStabilityError *
            state.Profile.PitchStability *
            tailStabilityAuthority;
        yawAcceleration +=
            state.SideslipAngle *
            state.Profile.YawStability *
            tailStabilityAuthority;

        pitchAcceleration +=
            -localAngular.x *
            Mathf.Rad2Deg *
            state.Profile.PitchDamping *
            tailStabilityAuthority;
        yawAcceleration +=
            -localAngular.y *
            Mathf.Rad2Deg *
            state.Profile.YawDamping *
            tailStabilityAuthority;
        rollAcceleration +=
            -localAngular.z *
            Mathf.Rad2Deg *
            state.Profile.RollDamping *
            Mathf.Clamp01(qRatio / 1.5f);

        var localAngularAcceleration =
            new Vector3(
                pitchAcceleration,
                yawAcceleration,
                rollAcceleration) *
            Mathf.Deg2Rad;
        if (FiniteVector(localAngularAcceleration))
        {
            rigidbody.AddRelativeTorque(
                localAngularAcceleration,
                ForceMode.Acceleration);
        }
    }

    private static float HighSpeedControlHeaviness(
        AircraftFlightState state)
    {
        var ratio =
            state.AirspeedMs / Mathf.Max(1f, state.MaximumSpeedMs);
        return Mathf.Lerp(
            1f,
            0.48f,
            SmoothStep01(Mathf.InverseLerp(0.72f, 1.25f, ratio)));
    }

    private static void UpdateDamageState(
        VehiclePlane plane,
        AircraftFlightState state)
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
        state.PropellerLoss = DetachedFraction(
            plane.detachablePropellers);
    }

    private static void RestoreNativeValues(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        if (!state.NativeValuesOverridden)
            return;

        state.NativeValuesOverridden = false;
        try
        {
            plane.maxKmhSpeed = state.OriginalMaximumSpeedKmh;
            plane.clocheMultiplier = state.OriginalClocheMultiplier;
            plane.timeFromZeroToMaxThrust =
                state.OriginalThrustResponseSeconds;
            plane.thrustForceMultiplier =
                state.OriginalThrustForceMultiplier;
            plane.startLiftMult = state.OriginalStartLiftMultiplier;
            plane.endLiftMult = state.OriginalEndLiftMultiplier;
            var rigidbody = plane.GetRigidbody();
            if (rigidbody != null)
            {
                rigidbody.drag = state.OriginalRigidbodyDrag;
                rigidbody.angularDrag =
                    state.OriginalRigidbodyAngularDrag;
                // Native-recovery tuning never owns interpolation. The point-
                // aim camera may be using it independently, so restoring the
                // value captured before camera acquisition would silently
                // disable smoothing while that camera still owns the view.
                if (UseReplacementFlightModel)
                {
                    rigidbody.interpolation =
                        state.OriginalRigidbodyInterpolation;
                }
            }
        }
        catch
        {
            // The object may already be in its destruction path.
        }
    }

    private static void WriteTelemetry(
        VehiclePlane plane,
        AircraftFlightState state)
    {
        if (!Settings.AircraftPhysicsTelemetryEnabled.Value ||
            Time.unscaledTime < state.NextTelemetry)
        {
            return;
        }

        state.NextTelemetry =
            Time.unscaledTime +
            Mathf.Max(
                0.05f,
                Settings.AircraftPhysicsTelemetryInterval.Value);
        var clearance = TerrainClearanceAhead(plane, state);
        AiState.Trace(
            $"Aircraft FM {SafeName(plane)} {state.Profile.Name}: " +
            $"speed={state.AirspeedMs * 3.6f:0}km/h " +
            $"forward={state.ForwardSpeedMs * 3.6f:0}km/h " +
            $"vertical={state.VerticalSpeedMs:0.0}m/s " +
            $"aoa={state.AngleOfAttack:0.0} beta={state.SideslipAngle:0.0} " +
            $"q={state.DynamicPressure:0}Pa " +
            $"cmd=P{state.CommandPitch:0.00}/R{state.CommandRoll:0.00}/Y{state.CommandYaw:0.00} " +
            $"authority=P{state.PitchAuthority:0.00}/R{state.RollAuthority:0.00}/Y{state.YawAuthority:0.00} " +
            $"stall={state.StallSeverity:0.00} spin={state.SpinSeverity:0.00} " +
            $"engine={state.EngineSpool:0.00} " +
            $"thrust={state.AvailableEngineAccelerationMs2:0.00}m/s2 " +
            $"drag={state.AdditionalDragAcceleration:0.00}m/s2 " +
            $"agl={(clearance >= 0f ? clearance.ToString("0") : "unknown")}m " +
            $"wingL={(state.LeftWingLost ? "lost" : "ok")} " +
            $"wingR={(state.RightWingLost ? "lost" : "ok")} " +
            $"tailLoss={state.TailLoss:0.00} propLoss={state.PropellerLoss:0.00}");
    }

    private static bool TryGetOwnedState(
        VehiclePlane plane,
        out AircraftFlightState state)
    {
        state = null!;
        if (plane == null || !ShouldApply(plane))
            return false;

        if (!States.TryGetValue(plane.GetInstanceID(), out var found))
        {
            Initialize(plane);
            if (!States.TryGetValue(plane.GetInstanceID(), out found))
                return false;
        }

        state = found;
        return true;
    }

    private static bool OwnsCurrentNativeFixedUpdate(
        VehiclePlane plane)
    {
        if (_nativeFixedUpdateOwner == null || plane == null)
            return false;

        try
        {
            return
                _nativeFixedUpdateOwner.GetInstanceID() ==
                plane.GetInstanceID();
        }
        catch
        {
            return false;
        }
    }

    private static float AirDensityMultiplier(VehiclePlane plane)
    {
        try
        {
            var multiplier = plane.airDensityMult;
            if (ValidPositive(multiplier))
                return Mathf.Clamp(multiplier, 0.35f, 1.10f);
        }
        catch
        {
            // Use sea-level density if native density is unavailable.
        }

        return 1f;
    }

    private static float AirDensity(VehiclePlane plane)
        => SeaLevelDensity * AirDensityMultiplier(plane);

    private static bool IsDetached(
        VehicleDamagableDetachablePart? part)
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

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float Approach(
        float current,
        float target,
        float responsePerSecond,
        float deltaTime)
        => Mathf.Lerp(
            current,
            target,
            1f -
            Mathf.Exp(
                -Mathf.Max(0f, responsePerSecond) *
                Mathf.Max(0f, deltaTime)));

    private static float FiniteClamped(float value)
        => float.IsFinite(value)
            ? Mathf.Clamp(value, -1f, 1f)
            : 0f;

    private static bool FiniteVector(Vector3 value)
        => float.IsFinite(value.x) &&
           float.IsFinite(value.y) &&
           float.IsFinite(value.z);

    private static bool ValidPositive(float value)
        => value > 0.01f && float.IsFinite(value);

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

    private readonly record struct WingPanelResult(
        float Separation,
        float Drag);
}

internal readonly record struct AircraftNativeFixedUpdateSnapshot(
    float OriginalFallDrag,
    bool Owned);

internal readonly record struct AircraftNativeUpdateSnapshot(
    float OriginalYaw,
    float OriginalPitch,
    float OriginalRoll,
    bool Overridden);

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
    private static void Prefix(
        VehiclePlane __instance,
        out AircraftNativeFixedUpdateSnapshot __state)
    {
        var timer = ModTimeProbe.Begin();
        try
        {
            __state =
                AircraftFlightPhysics.BeginNativeFixedUpdate(__instance);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, timer);
        }
    }

    [HarmonyPostfix]
    private static void Postfix(
        VehiclePlane __instance,
        AircraftNativeFixedUpdateSnapshot __state)
    {
        var timer = ModTimeProbe.Begin();
        try
        {
            AircraftFlightPhysics.EndNativeFixedUpdate(__state);
            AircraftFlightPhysics.FixedUpdate(
                __instance,
                __state.Owned);
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, timer);
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(
        AircraftNativeFixedUpdateSnapshot __state)
        => AircraftFlightPhysics.EndNativeFixedUpdate(__state);
}

[HarmonyPatch(typeof(VehiclePlane), "Update")]
internal static class AircraftNativeControlAuthorityPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance,
        out AircraftNativeUpdateSnapshot __state)
        => __state =
            AircraftFlightPhysics.BeginNativeUpdate(__instance);

    [HarmonyPostfix]
    private static void Postfix(
        VehiclePlane __instance,
        AircraftNativeUpdateSnapshot __state)
        => AircraftFlightPhysics.EndNativeUpdate(
            __instance,
            __state);

    [HarmonyFinalizer]
    private static void Finalizer(
        VehiclePlane __instance,
        AircraftNativeUpdateSnapshot __state)
        => AircraftFlightPhysics.EndNativeUpdate(
            __instance,
            __state);
}

[HarmonyPatch(
    typeof(VehiclePlane),
    "RefreshThrustForce",
    new[] { typeof(bool) })]
internal static class AircraftThrottleSpeedGovernorPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance,
        ref bool canApplyThrust)
        => AircraftFlightPhysics.DecoupleThrottleSpeedGovernor(
            __instance,
            ref canApplyThrust);
}

[HarmonyPatch(typeof(VehiclePlane), nameof(VehiclePlane.OpenGear))]
internal static class AircraftLandingGearSpeedInterlockPatch
{
    [HarmonyPrefix]
    private static bool Prefix(VehiclePlane __instance)
        => AircraftFlightPhysics.AllowLandingGearExtension(__instance);
}

[HarmonyPatch(
    typeof(RigidbodyCompat),
    nameof(RigidbodyCompat.SetVelocity),
    new[] { typeof(Rigidbody), typeof(Vector3) })]
internal static class AircraftNativeVelocityWritePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        Rigidbody rb,
        ref Vector3 velocity)
        => AircraftFlightPhysics.FilterNativeVelocityWrite(
            rb,
            ref velocity);
}

/// <summary>
/// Restores pitch and roll while a physical-controller player is using the
/// game's vehicle-look branch, which otherwise hardcodes both axes to zero.
/// </summary>
internal static class AircraftFreeLookSteering
{
    private const float PitchAxisDeadZone = 0.01f;
    private static bool _loggedFailure;
    private static bool _holdingThrottle;
    private static float _heldThrottle;

    internal static void RestoreSuppressedInputs(
        VehiclePlane plane,
        ref float yaw,
        ref float pitch,
        ref float roll)
    {
        if (!Settings.AircraftFreeLookSteeringEnabled.Value ||
            pitch != 0f ||
            roll != 0f)
        {
            return;
        }

        try
        {
            var controller = PlayerController.currentController;
            if (!IsFreeLookHeld() ||
                controller == null ||
                plane == null ||
                controller.ControlledVehicle?.GetInstanceID() !=
                plane.GetInstanceID())
            {
                _holdingThrottle = false;
                return;
            }

            var input =
                PlayerController.GetGenericCameraRotationInput(
                    PlaneYAxisDirection(),
                    1,
                    swapSticks: true);
            var multiplier =
                PlayerController.InputUpdateMultiplier() *
                controller.AimMultiplier();
            pitch = input.x * multiplier;
            roll = input.y * multiplier;

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

    private static void HoldThrottleWhilePitching(
        VehiclePlane plane,
        float pitchAxis)
    {
        if (Mathf.Abs(pitchAxis) <= PitchAxisDeadZone)
        {
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
                   GameInput.LookAroundInVehicle,
                   StickPressCondition.StickCentered);
    }
}

internal static class AircraftControlSelection
{
    internal static bool IsSimplifiedSelected()
    {
        try
        {
            var controls = SavableData.Settings?.controls;
            return controls != null && controls.simplifiedPlaneControls;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsRealisticSelected()
    {
        try
        {
            var controls = SavableData.Settings?.controls;
            return controls != null && !controls.simplifiedPlaneControls;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Adds the player's configured axial-roll keys to the native Simplified
/// attitude solver. GetFixedTargetRotation calculates its automatic bank in
/// rollAngle immediately before FixedUpdate consumes it, so replacing that one
/// value while a key is held preserves native mouse guidance and auto-leveling.
/// </summary>
internal static class AircraftSimplifiedManualRoll
{
    private const float ManualBankDegrees = 65f;
    private const float InputDeadZone = 0.01f;
    private static bool _loggedFailure;

    internal static void Apply(VehiclePlane plane)
    {
        if (!Settings.AircraftSimplifiedManualRollEnabled.Value)
            return;

        try
        {
            var controller = PlayerController.currentController;
            if (plane == null ||
                controller == null ||
                controller.ControlledVehicle?.GetInstanceID() !=
                    plane.GetInstanceID() ||
                !AircraftControlSelection.IsSimplifiedSelected())
            {
                return;
            }

            var input = GamepadsAPI.GetGamepad(0);
            if (input == null || input.IsGamepad)
                return;

            var roll = 0f;
            if (input.GetButton(
                    GameInput.realisticPlane_roll,
                    StickPressCondition.StickCentered))
            {
                roll += 1f;
            }

            if (input.GetButton(
                    GameInput.realisticPlane_rollNegative,
                    StickPressCondition.StickCentered))
            {
                roll -= 1f;
            }

            if (Mathf.Abs(roll) <= InputDeadZone)
                return;

            // Native FixedUpdate negates rollAngle before applying its local-Z
            // bank, so positive remains right roll and negative remains left.
            // On release this override stops and the native Simplified solver
            // immediately resumes its own coordinated-bank/auto-level target.
            plane.rollAngle = Mathf.Clamp(roll, -1f, 1f) *
                              ManualBankDegrees;
        }
        catch (Exception ex)
        {
            if (_loggedFailure)
                return;

            _loggedFailure = true;
            Plugin.LogSource.LogWarning(
                $"Simplified aircraft manual roll disabled after an input failure: {ex.Message}");
        }
    }
}

[HarmonyPatch(
    typeof(VehiclePlane),
    nameof(VehiclePlane.GetFixedTargetRotation))]
internal static class AircraftSimplifiedManualRollPatch
{
    [HarmonyPostfix]
    private static void Postfix(VehiclePlane __instance)
        => AircraftSimplifiedManualRoll.Apply(__instance);
}

[HarmonyPatch(
    typeof(VehiclePlane),
    nameof(VehiclePlane.RotateRealisticJoystick))]
internal static class AircraftJoystickSurfacePatch
{
    [HarmonyPrefix]
    private static void Prefix(
        VehiclePlane __instance,
        ref float yaw,
        ref float pitch,
        ref float roll)
    {
        // This method is also the direct Realistic Keyboard path. Mouse point
        // aim redirects its instructor command here explicitly from the mouse
        // prefix below; never replace unrelated joystick/keyboard calls.
        AircraftFreeLookSteering.RestoreSuppressedInputs(
            __instance,
            ref yaw,
            ref pitch,
            ref roll);
    }

    [HarmonyPostfix]
    private static void Postfix(VehiclePlane __instance)
        => AircraftFlightPhysics.CaptureNativeSurfaceState(__instance);
}

[HarmonyPatch(
    typeof(VehiclePlane),
    nameof(VehiclePlane.RotateRealisticMouse))]
internal static class AircraftMouseSurfacePatch
{
    [HarmonyPrefix]
    private static bool Prefix(
        VehiclePlane __instance,
        ref float yaw,
        ref float add_pitch,
        ref float add_roll)
    {
        AircraftMousePointAiming.CaptureNativeMouseInput(
            __instance,
            yaw,
            add_pitch,
            add_roll);

        if (!AircraftMousePointAiming.TryGetGuidanceInputs(
                __instance,
                out var guidedYaw,
                out var guidedPitch,
                out var guidedRoll))
        {
            return true;
        }

        // Both control modes end in the same native virtual-surface filter.
        __instance.RotateRealisticJoystick(
            guidedYaw,
            guidedPitch,
            guidedRoll);
        return false;
    }

    [HarmonyPostfix]
    private static void Postfix(VehiclePlane __instance)
        => AircraftFlightPhysics.CaptureNativeSurfaceState(__instance);
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
