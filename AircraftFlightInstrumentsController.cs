using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class AircraftFlightInstrumentsController : MonoBehaviour
{
    private static readonly Color CardColor = new(0.025f, 0.040f, 0.050f, 0.82f);
    private static readonly Color CardRaisedColor = new(0.055f, 0.080f, 0.092f, 0.90f);
    private static readonly Color BorderColor = new(0.30f, 0.43f, 0.46f, 0.90f);
    private static readonly Color TextColor = new(0.90f, 0.96f, 0.95f, 1f);
    private static readonly Color MutedColor = new(0.58f, 0.70f, 0.71f, 1f);
    private static readonly Color NormalAccent = new(0.32f, 0.78f, 0.72f, 1f);
    private static readonly Color CautionAccent = new(1f, 0.67f, 0.20f, 1f);
    private static readonly Color DangerAccent = new(0.96f, 0.25f, 0.18f, 1f);

    private VehiclePlane? _plane;
    private float _nextAcquireTime;
    private float _nextAglSampleTime;
    private float _speedMs;
    private float _verticalSpeedMs;
    private float _altitudeMeters;
    private float _aglMeters = -1f;
    private float _maximumSpeedMs = 140f;
    private float _stallSpeedMs;
    private float _stallSeverity;
    private bool _isSpinning;
    private bool _hasFlightSample;
    private string _lastErrorSignature = string.Empty;
    private float _styleScale;
    private GUIStyle? _titleStyle;
    private GUIStyle? _valueStyle;
    private GUIStyle? _unitStyle;
    private GUIStyle? _secondaryStyle;
    private GUIStyle? _warningStyle;

    private void Update()
    {
        if (!Settings.AircraftInstrumentHudEnabled.Value)
        {
            ClearPlane();
            return;
        }

        try
        {
            if (_plane != null && !IsValidLocalPlane(_plane))
                ClearPlane();

            if (_plane == null && Time.unscaledTime >= _nextAcquireTime)
            {
                _nextAcquireTime = Time.unscaledTime + 0.20f;
                _plane = FindLocalPlayerPlane();
                _hasFlightSample = false;
                _aglMeters = -1f;
            }

            if (_plane == null)
                return;

            var rigidbody = _plane.GetRigidbody();
            if (rigidbody == null)
            {
                ClearPlane();
                return;
            }

            var rawSpeed = rigidbody.velocity.magnitude;
            var rawVerticalSpeed = rigidbody.velocity.y;
            var rawAltitude = _plane.transform.position.y;
            if (!_hasFlightSample)
            {
                _speedMs = rawSpeed;
                _verticalSpeedMs = rawVerticalSpeed;
                _altitudeMeters = rawAltitude;
                _hasFlightSample = true;
            }
            else
            {
                var smoothing = 1f - Mathf.Exp(-8f * Mathf.Max(0f, Time.unscaledDeltaTime));
                _speedMs = Mathf.Lerp(_speedMs, rawSpeed, smoothing);
                _verticalSpeedMs = Mathf.Lerp(_verticalSpeedMs, rawVerticalSpeed, smoothing);
                _altitudeMeters = Mathf.Lerp(_altitudeMeters, rawAltitude, smoothing);
            }

            _maximumSpeedMs = Mathf.Max(30f, _plane.maxKmhSpeed / 3.6f);
            _stallSpeedMs = 0f;
            _stallSeverity = 0f;
            _isSpinning = false;
            if (AircraftFlightPhysics.TryGetState(_plane, out var state))
            {
                _maximumSpeedMs = Mathf.Max(30f, state.MaximumSpeedMs);
                _stallSpeedMs = state.StallSpeedMs;
                _stallSeverity = state.StallSeverity;
                _isSpinning = state.IsSpinning || state.SpinSeverity > 0.18f;
            }

            if (Time.unscaledTime >= _nextAglSampleTime)
            {
                _nextAglSampleTime = Time.unscaledTime + 0.20f;
                _aglMeters = SampleAltitudeAboveGround(_plane);
            }
        }
        catch (Exception ex)
        {
            ReportError("sampling", ex);
            ClearPlane();
        }
    }

    private void OnGUI()
    {
        if (!Settings.AircraftInstrumentHudEnabled.Value ||
            _plane == null || !_hasFlightSample ||
            Event.current == null || Event.current.type != EventType.Repaint ||
            IsMenuOrPauseVisible())
        {
            return;
        }

        try
        {
            GUI.depth = -500;
            var screenScale = Mathf.Clamp(
                Mathf.Min(Screen.width / 1920f, Screen.height / 1080f), 0.75f, 2f);
            var scale = screenScale * Settings.AircraftInstrumentHudScale.Value;
            EnsureStyles(scale);

            var width = 176f * scale;
            var speedHeight = 92f * scale;
            var altitudeHeight = 112f * scale;
            var gap = 8f * scale;
            var totalHeight = speedHeight + gap + altitudeHeight;
            var x = 22f * scale;
            var y = Mathf.Clamp(
                Screen.height * 0.48f - totalHeight * 0.5f,
                60f * scale,
                Screen.height - totalHeight - 30f * scale);

            DrawSpeedGauge(new Rect(x, y, width, speedHeight), scale);
            DrawAltitudeGauge(
                new Rect(x, y + speedHeight + gap, width, altitudeHeight), scale);
        }
        catch (Exception ex)
        {
            ReportError("rendering", ex);
        }
    }

    [HideFromIl2Cpp]
    private static VehiclePlane? FindLocalPlayerPlane()
    {
        try
        {
            var soldier = Soldier.CurrentControlledSoldierOrNull();
            var vehicle = soldier?.GetCurrentVehicle();
            var plane = vehicle?.GetComponent<VehiclePlane>();
            if (plane != null && IsValidLocalPlane(plane))
                return plane;
        }
        catch
        {
            // Seat transitions can briefly invalidate the controlled-soldier wrapper.
        }

        try
        {
            var vehicles = Vehicle.allVehicles;
            if (vehicles == null)
                return null;

            for (var index = 0; index < vehicles.Count; index++)
            {
                var vehicle = vehicles[index];
                var plane = vehicle?.GetComponent<VehiclePlane>();
                if (plane != null && IsValidLocalPlane(plane))
                    return plane;
            }
        }
        catch
        {
            // The native vehicle list can change while a mission is loading or unloading.
        }

        return null;
    }

    [HideFromIl2Cpp]
    private static bool IsValidLocalPlane(VehiclePlane plane)
    {
        try
        {
            return plane != null && plane.life > 0 && plane.IsLocalPlayerDriving();
        }
        catch
        {
            return false;
        }
    }

    [HideFromIl2Cpp]
    private static float SampleAltitudeAboveGround(VehiclePlane plane)
    {
        var aircraftPosition = plane.transform.position;
        var origin = aircraftPosition + Vector3.up * 2f;
        const float maximumDistance = 10000f;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (!Physics.Raycast(
                    origin,
                    Vector3.down,
                    out var hit,
                    maximumDistance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                return -1f;
            }

            var hitTransform = hit.collider != null ? hit.collider.transform : null;
            var hitOwnAircraft = hitTransform != null &&
                                 (hitTransform == plane.transform ||
                                  hitTransform.IsChildOf(plane.transform));
            if (!hitOwnAircraft)
                return Mathf.Max(0f, aircraftPosition.y - hit.point.y);

            origin = hit.point + Vector3.down * 0.05f;
        }

        return -1f;
    }

    [HideFromIl2Cpp]
    private void DrawSpeedGauge(Rect rect, float scale)
    {
        var accent = _isSpinning
            ? DangerAccent
            : _stallSeverity >= 0.55f
                ? DangerAccent
                : _stallSeverity >= 0.20f
                    ? CautionAccent
                    : NormalAccent;
        DrawCard(rect, accent, scale);

        var padding = 10f * scale;
        GUI.Label(
            new Rect(rect.x + padding, rect.y + 3f * scale, rect.width - padding * 2f, 20f * scale),
            "AIRSPEED",
            _titleStyle!);
        var warning = _isSpinning ? "SPIN" : _stallSeverity >= 0.55f ? "STALL" : string.Empty;
        if (!string.IsNullOrEmpty(warning))
        {
            GUI.Label(
                new Rect(rect.xMax - 65f * scale, rect.y + 3f * scale, 55f * scale, 20f * scale),
                warning,
                _warningStyle!);
        }

        var imperial = Settings.AircraftInstrumentUseImperialUnits.Value;
        var displayedSpeed = _speedMs * (imperial ? 1.9438445f : 3.6f);
        GUI.Label(
            new Rect(rect.x + padding, rect.y + 20f * scale, 92f * scale, 42f * scale),
            Mathf.RoundToInt(displayedSpeed).ToString(),
            _valueStyle!);
        GUI.Label(
            new Rect(rect.x + 103f * scale, rect.y + 30f * scale, 58f * scale, 25f * scale),
            imperial ? "KTS" : "KM/H",
            _unitStyle!);

        var track = new Rect(
            rect.x + padding, rect.yMax - 17f * scale, rect.width - padding * 2f, 6f * scale);
        FillRect(track, CardRaisedColor);
        var speedFraction = Mathf.Clamp01(_speedMs / Mathf.Max(1f, _maximumSpeedMs));
        FillRect(new Rect(track.x, track.y, track.width * speedFraction, track.height), accent);
        if (_stallSpeedMs > 0f)
        {
            var stallMarker = Mathf.Clamp01(_stallSpeedMs / Mathf.Max(1f, _maximumSpeedMs));
            FillRect(
                new Rect(track.x + track.width * stallMarker - scale, track.y - 2f * scale, 2f * scale, 10f * scale),
                DangerAccent);
        }
    }

    [HideFromIl2Cpp]
    private void DrawAltitudeGauge(Rect rect, float scale)
    {
        DrawCard(rect, NormalAccent, scale);
        var padding = 10f * scale;
        GUI.Label(
            new Rect(rect.x + padding, rect.y + 3f * scale, rect.width - padding * 2f, 20f * scale),
            "ALTITUDE",
            _titleStyle!);

        var imperial = Settings.AircraftInstrumentUseImperialUnits.Value;
        var altitude = _altitudeMeters * (imperial ? 3.28084f : 1f);
        GUI.Label(
            new Rect(rect.x + padding, rect.y + 20f * scale, 102f * scale, 42f * scale),
            Mathf.RoundToInt(altitude).ToString(),
            _valueStyle!);
        GUI.Label(
            new Rect(rect.x + 113f * scale, rect.y + 30f * scale, 48f * scale, 25f * scale),
            imperial ? "FT" : "M",
            _unitStyle!);

        var secondaryY = rect.y + 65f * scale;
        if (Settings.AircraftInstrumentShowAgl.Value)
        {
            var aglText = _aglMeters >= 0f
                ? $"AGL  {Mathf.RoundToInt(_aglMeters * (imperial ? 3.28084f : 1f))} {(imperial ? "ft" : "m")}"
                : "AGL  ---";
            GUI.Label(
                new Rect(rect.x + padding, secondaryY, rect.width - padding * 2f, 20f * scale),
                aglText,
                _secondaryStyle!);
            secondaryY += 19f * scale;
        }

        var verticalSpeed = imperial ? _verticalSpeedMs * 196.8504f : _verticalSpeedMs;
        var verticalUnit = imperial ? "ft/min" : "m/s";
        GUI.Label(
            new Rect(rect.x + padding, secondaryY, rect.width - padding * 2f, 20f * scale),
            $"V/S  {verticalSpeed:+0;-0;0} {verticalUnit}",
            _secondaryStyle!);
    }

    [HideFromIl2Cpp]
    private static void DrawCard(Rect rect, Color accent, float scale)
    {
        FillRect(rect, CardColor);
        DrawOutline(rect, BorderColor, Mathf.Max(1f, scale));
        FillRect(new Rect(rect.x, rect.y, 4f * scale, rect.height), accent);
    }

    [HideFromIl2Cpp]
    private void EnsureStyles(float scale)
    {
        if (_titleStyle != null && Mathf.Abs(scale - _styleScale) < 0.02f)
            return;

        _styleScale = scale;
        _titleStyle = CloneStyle(GUI.skin.label);
        _titleStyle.fontSize = FontSize(11f, scale);
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColor(_titleStyle, MutedColor);

        _valueStyle = CloneStyle(GUI.skin.label);
        _valueStyle.fontSize = FontSize(29f, scale);
        _valueStyle.fontStyle = FontStyle.Bold;
        _valueStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColor(_valueStyle, TextColor);

        _unitStyle = CloneStyle(GUI.skin.label);
        _unitStyle.fontSize = FontSize(11f, scale);
        _unitStyle.fontStyle = FontStyle.Bold;
        _unitStyle.alignment = TextAnchor.MiddleRight;
        SetTextColor(_unitStyle, MutedColor);

        _secondaryStyle = CloneStyle(GUI.skin.label);
        _secondaryStyle.fontSize = FontSize(11f, scale);
        _secondaryStyle.fontStyle = FontStyle.Bold;
        _secondaryStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColor(_secondaryStyle, TextColor);

        _warningStyle = CloneStyle(GUI.skin.label);
        _warningStyle.fontSize = FontSize(11f, scale);
        _warningStyle.fontStyle = FontStyle.Bold;
        _warningStyle.alignment = TextAnchor.MiddleRight;
        SetTextColor(_warningStyle, DangerAccent);
    }

    [HideFromIl2Cpp]
    private static int FontSize(float points, float scale)
        => Mathf.Max(9, Mathf.RoundToInt(points * scale));

    [HideFromIl2Cpp]
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        var style = new GUIStyle();
        GUIStyle.Internal_Copy(style, source);
        return style;
    }

    [HideFromIl2Cpp]
    private static void SetTextColor(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
    }

    [HideFromIl2Cpp]
    private static void FillRect(Rect rect, Color color)
    {
        var previousColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private static void DrawOutline(Rect rect, Color color, float thickness)
    {
        thickness = Mathf.Max(1f, thickness);
        FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    [HideFromIl2Cpp]
    private static bool IsMenuOrPauseVisible()
    {
        try
        {
            return MainMenu.instance != null || (Pause.instance != null && Pause.IsPaused());
        }
        catch
        {
            return false;
        }
    }

    [HideFromIl2Cpp]
    private void ClearPlane()
    {
        _plane = null;
        _hasFlightSample = false;
        _aglMeters = -1f;
        _stallSeverity = 0f;
        _isSpinning = false;
    }

    [HideFromIl2Cpp]
    private void ReportError(string operation, Exception exception)
    {
        var signature = operation + ":" + exception.GetType().FullName + ":" + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Aircraft instrument {operation} failed (further identical errors suppressed): {exception.Message}");
    }
}
