using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class PlayerViewFeaturesController : MonoBehaviour
{
    private const float FreeLookVerticalLimitDegrees = 60f;
    private const float FreeLookReturnSpeedDegreesPerSecond = 720f;
    private const float OverlayRadiusHeightFraction = 0.49f;
    private const float OverlayLensOffsetWidthFraction = 0.22f;
    private const float OverlayStripHeight = 2f;
    private const float CompassVisibleArcDegrees = 120f;
    private const float CompassRevealDurationSeconds = 5f;
    private const float CompassEdgeFadeFraction = 0.16f;
    private const float CompassBackgroundAlpha = 0.62f;
    private const int CompassBackgroundFadeSteps = 20;
    private const float MilsPerCircle = 6400f;

    private static PlayerViewFeaturesController? _instance;

    private bool _binocularsActive;
    private bool _compassVisible;
    private bool _freeLookHeld;
    private float _binocularBaseFov = float.NaN;
    private int _binocularCameraInstanceId;
    private FPSGunManager? _hiddenFpsGun;
    private bool _hasMapNorthShift;
    private float _mapNorthShiftDegrees;
    private int _mapNorthTerrainInstanceId;
    private float _compassVisibleUntil = float.NegativeInfinity;
    private float _freeLookPitch;
    private float _freeLookYaw;
    private int _lastRotationCaptureFrame = -1;
    private string _lastErrorSignature = string.Empty;
    private GUIStyle? _compassLabelStyle;

    internal static bool BinocularsActive => _instance != null && _instance._binocularsActive;

    private void Awake()
    {
        _instance = this;
    }

    private void Update()
    {
        try
        {
            RefreshMapNorthShift();

            if (!CanShowCompass())
            {
                _compassVisible = false;
                ResetOpticsAndFreeLook();
                return;
            }

            if (Input.GetKeyDown(Settings.CompassKey.Value))
                _compassVisibleUntil = Time.unscaledTime + CompassRevealDurationSeconds;

            _compassVisible = Settings.CompassAlwaysVisible.Value ||
                              Time.unscaledTime < _compassVisibleUntil;

            if (!CanUseFirstPersonViewFeatures())
            {
                ResetOpticsAndFreeLook();
                return;
            }

            if (!Settings.BinocularsEnabled.Value)
            {
                SetBinocularsActive(false);
            }
            else if (Input.GetKeyDown(Settings.BinocularsKey.Value))
            {
                SetBinocularsActive(!_binocularsActive);
            }

            _freeLookHeld = Settings.FreeLookEnabled.Value && IsFreeLookKeyHeld();
            if (!_freeLookHeld)
            {
                var returnStep = FreeLookReturnSpeedDegreesPerSecond * Time.unscaledDeltaTime;
                _freeLookPitch = Mathf.MoveTowards(_freeLookPitch, 0f, returnStep);
                _freeLookYaw = Mathf.MoveTowards(_freeLookYaw, 0f, returnStep);
            }
        }
        catch (Exception ex)
        {
            ResetViewFeatures();
            ReportError(ex);
        }
    }

    private void OnGUI()
    {
        if ((!_binocularsActive && !_compassVisible) ||
            Event.current == null ||
            Event.current.type != EventType.Repaint)
            return;

        try
        {
            if (_binocularsActive)
                DrawBinocularOverlay();

            if (_compassVisible)
                DrawCompass();
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void OnDestroy()
    {
        ResetViewFeatures();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }

    [HideFromIl2Cpp]
    internal static float GetBinocularFov(float nativeFov)
    {
        var magnification = Mathf.Max(1f, Settings.BinocularZoomMultiplier.Value);
        var halfAngleRadians = nativeFov * Mathf.Deg2Rad * 0.5f;
        return Mathf.Clamp(
            2f * Mathf.Atan(Mathf.Tan(halfAngleRadians) / magnification) * Mathf.Rad2Deg,
            1f,
            179f);
    }

    [HideFromIl2Cpp]
    internal static void ApplyBinocularView()
    {
        var instance = _instance;
        if (instance == null || !instance._binocularsActive)
            return;

        try
        {
            if (!CanUseFirstPersonViewFeatures())
            {
                instance.SetBinocularsActive(false);
                return;
            }

            var camera = ResourcesManager.mainCamera;
            if (camera == null)
                return;

            var cameraInstanceId = camera.GetInstanceID();
            if (cameraInstanceId != instance._binocularCameraInstanceId ||
                float.IsNaN(instance._binocularBaseFov))
            {
                instance._binocularCameraInstanceId = cameraInstanceId;
                instance._binocularBaseFov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);
            }

            // Run after the game's PlayerController update so the native FOV
            // interpolation cannot overwrite the binocular magnification.
            camera.fieldOfView = GetBinocularFov(instance._binocularBaseFov);
            instance.HideCurrentViewModel();
        }
        catch (Exception ex)
        {
            instance.ResetOpticsAndFreeLook();
            instance.ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    internal static void CaptureMapNorthShift(MiniMapGUI miniMap)
    {
        _instance?.CacheMapNorthShift(miniMap);
    }

    [HideFromIl2Cpp]
    internal static void CaptureAndSuppressRotation(ref Vector2 rotationInput)
    {
        var instance = _instance;
        if (instance == null ||
            !Settings.FreeLookEnabled.Value ||
            !IsFreeLookKeyHeld() ||
            !CanUseFirstPersonViewFeatures())
            return;

        try
        {
            instance._freeLookHeld = true;

            // GetCameraRotationInput already contains the player's mouse/controller
            // sensitivity and inversion. Match the game's final per-frame multiplier,
            // then prevent that same input from turning the soldier's body and weapon.
            if (instance._lastRotationCaptureFrame != Time.frameCount)
            {
                instance._lastRotationCaptureFrame = Time.frameCount;
                var multiplier = PlayerController.InputUpdateMultiplier();
                var horizontalLimit = Mathf.Clamp(
                    Settings.FreeLookHorizontalArcDegrees.Value,
                    1f,
                    359f) * 0.5f;

                instance._freeLookPitch = Mathf.Clamp(
                    instance._freeLookPitch + rotationInput.x * multiplier,
                    -FreeLookVerticalLimitDegrees,
                    FreeLookVerticalLimitDegrees);
                instance._freeLookYaw = Mathf.Clamp(
                    instance._freeLookYaw + rotationInput.y * multiplier,
                    -horizontalLimit,
                    horizontalLimit);
            }

            rotationInput = Vector2.zero;
        }
        catch (Exception ex)
        {
            instance.ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    internal static void ApplyFreeLook(Soldier soldier)
    {
        var instance = _instance;
        if (instance == null || soldier == null)
            return;

        try
        {
            var controlled = Soldier.CurrentControlledSoldierOrNull();
            if (controlled == null ||
                controlled.GetInstanceID() != soldier.GetInstanceID() ||
                !CanUseFirstPersonViewFeatures())
                return;

            if (Mathf.Abs(instance._freeLookPitch) < 0.01f &&
                Mathf.Abs(instance._freeLookYaw) < 0.01f)
                return;

            var camera = ResourcesManager.mainCamera;
            if (camera == null)
                return;

            camera.transform.rotation *= Quaternion.Euler(
                instance._freeLookPitch,
                instance._freeLookYaw,
                0f);
        }
        catch (Exception ex)
        {
            instance.ReportError(ex);
        }
    }

    [HideFromIl2Cpp]
    private static bool CanUseFirstPersonViewFeatures()
    {
        if (!PlayerController.fpsCamera || ResourcesManager.UsingGUIExceptEndBattle())
            return false;

        var soldier = Soldier.CurrentControlledSoldierOrNull();
        return soldier != null && soldier.NotDeadAndSurrendered() && !soldier.IsOnVehicle();
    }

    [HideFromIl2Cpp]
    private static bool CanShowCompass()
    {
        if (ResourcesManager.UsingGUIExceptEndBattle())
            return false;

        var soldier = Soldier.CurrentControlledSoldierOrNull();
        return soldier != null && soldier.NotDeadAndSurrendered();
    }

    [HideFromIl2Cpp]
    private static bool IsFreeLookKeyHeld() =>
        Input.GetKey(Settings.FreeLookKey.Value) ||
        (Settings.FreeLookKey.Value == KeyCode.LeftAlt && Input.GetKey(KeyCode.RightAlt));

    [HideFromIl2Cpp]
    private void DrawBinocularOverlay()
    {
        var width = (float)Screen.width;
        var height = (float)Screen.height;
        if (width <= 0f || height <= 0f)
            return;

        var radius = height * OverlayRadiusHeightFraction;
        var centerX = width * 0.5f;
        var centerY = height * 0.5f;
        var lensOffset = Mathf.Min(width * OverlayLensOffsetWidthFraction, radius * 0.90f);

        var previousColor = GUI.color;
        GUI.color = Color.black;

        for (var y = 0f; y < height; y += OverlayStripHeight)
        {
            var stripHeight = Mathf.Min(OverlayStripHeight, height - y);
            var dy = y + stripHeight * 0.5f - centerY;
            var squaredSpan = radius * radius - dy * dy;

            if (squaredSpan <= 0f)
            {
                DrawBlackRect(0f, y, width, stripHeight);
                continue;
            }

            var span = Mathf.Sqrt(squaredSpan);
            var leftStart = centerX - lensOffset - span;
            var leftEnd = centerX - lensOffset + span;
            var rightStart = centerX + lensOffset - span;
            var rightEnd = centerX + lensOffset + span;

            DrawBlackRect(0f, y, Mathf.Max(0f, leftStart), stripHeight);

            if (leftEnd < rightStart)
            {
                DrawBlackRect(
                    Mathf.Max(0f, leftEnd),
                    y,
                    Mathf.Max(0f, rightStart - leftEnd),
                    stripHeight);
            }

            DrawBlackRect(
                Mathf.Min(width, rightEnd),
                y,
                Mathf.Max(0f, width - rightEnd),
                stripHeight);
        }

        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private void DrawCompass()
    {
        var camera = ResourcesManager.mainCamera;
        if (camera == null)
            return;

        var screenWidth = (float)Screen.width;
        var screenHeight = (float)Screen.height;
        if (screenWidth <= 0f || screenHeight <= 0f)
            return;

        var width = Mathf.Min(720f, screenWidth * 0.72f);
        const float height = 54f;
        var left = (screenWidth - width) * 0.5f;
        var top = screenHeight - height - 22f;
        var centerX = left + width * 0.5f;
        var forward = camera.transform.forward;
        var worldHeadingDegrees = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        RefreshMapNorthShift();
        var mapNorthShiftDegrees = _hasMapNorthShift ? _mapNorthShiftDegrees : 0f;
        var headingDegrees = Mathf.Repeat(worldHeadingDegrees - mapNorthShiftDegrees, 360f);
        var previousColor = GUI.color;

        DrawCompassBackground(left, top, width, height);

        EnsureCompassStyle();
        var style = _compassLabelStyle!;
        if (Settings.CompassUseMils.Value)
            DrawMilsCompassTape(left, top, width, centerX, headingDegrees, style);
        else
            DrawDegreesCompassTape(left, top, width, centerX, headingDegrees, style);

        GUI.color = new Color(1f, 0.84f, 0.25f, 1f);
        GUI.DrawTexture(new Rect(centerX - 1.5f, top, 3f, height), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    [HideFromIl2Cpp]
    private void RefreshMapNorthShift()
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            var terrainInstanceId = terrain.GetInstanceID();
            if (_mapNorthTerrainInstanceId != 0 &&
                terrainInstanceId != _mapNorthTerrainInstanceId)
            {
                _hasMapNorthShift = false;
            }

            _mapNorthTerrainInstanceId = terrainInstanceId;
        }

        // The day/night controller carries the current map's persistent north
        // direction from scene setup, so the compass is calibrated even when
        // the player has never opened the tactical map.
        var dayNightCycle = Corvostudio.SuperDayNightCycle.DayNightCycle.GetInstance();
        if (dayNightCycle != null)
        {
            _mapNorthShiftDegrees = dayNightCycle.northDirection;
            _hasMapNorthShift = true;
        }

        // When available, the tactical map's resolved value remains the
        // authoritative confirmation for both vanilla and custom maps.
        var miniMap = MiniMapGUI.Instance;
        if (miniMap != null)
            CacheMapNorthShift(miniMap);
    }

    [HideFromIl2Cpp]
    private void CacheMapNorthShift(MiniMapGUI miniMap)
    {
        if (miniMap == null)
            return;

        var terrain = miniMap.terrain;
        if (terrain != null)
            _mapNorthTerrainInstanceId = terrain.GetInstanceID();

        _mapNorthShiftDegrees = miniMap.northShift;
        _hasMapNorthShift = true;
    }

    [HideFromIl2Cpp]
    private static void DrawMilsCompassTape(
        float left,
        float top,
        float width,
        float centerX,
        float headingDegrees,
        GUIStyle style)
    {
        var headingMils = headingDegrees * MilsPerCircle / 360f;
        var visibleArcMils = CompassVisibleArcDegrees * MilsPerCircle / 360f;
        var pixelsPerMil = width / visibleArcMils;
        var firstMil = Mathf.Floor((headingMils - visibleArcMils * 0.5f) / 50f) * 50f;
        var lastMil = headingMils + visibleArcMils * 0.5f;

        // NATO angular mils: 6400 mils per circle, 50-mil minor marks,
        // 100-mil major marks, and numeric labels every 200 mils.
        for (var rawMil = firstMil; rawMil <= lastMil + 50f; rawMil += 50f)
        {
            var mil = Mathf.Repeat(rawMil, MilsPerCircle);
            var deltaMils = Mathf.Repeat(mil - headingMils + MilsPerCircle * 0.5f, MilsPerCircle) -
                            MilsPerCircle * 0.5f;
            var x = centerX + deltaMils * pixelsPerMil;
            if (x < left || x > left + width)
                continue;

            var roundedMil = Mathf.RoundToInt(mil) % (int)MilsPerCircle;
            var isCardinal = roundedMil % 800 == 0;
            var isMajor = roundedMil % 100 == 0;
            var edgeAlpha = CompassEdgeAlpha(x, left, width);
            DrawCompassTick(x, top, isCardinal, isMajor, edgeAlpha);

            if (isCardinal)
                DrawCompassLabel(x, top, CardinalLabel(roundedMil / 800 * 45), style, true, edgeAlpha);
            else if (roundedMil % 200 == 0)
                DrawCompassLabel(x, top, roundedMil.ToString(), style, false, edgeAlpha);
        }
    }

    [HideFromIl2Cpp]
    private static void DrawDegreesCompassTape(
        float left,
        float top,
        float width,
        float centerX,
        float headingDegrees,
        GUIStyle style)
    {
        var pixelsPerDegree = width / CompassVisibleArcDegrees;
        var firstDegree = Mathf.Floor((headingDegrees - CompassVisibleArcDegrees * 0.5f) / 5f) * 5f;
        var lastDegree = headingDegrees + CompassVisibleArcDegrees * 0.5f;

        for (var rawDegree = firstDegree; rawDegree <= lastDegree + 5f; rawDegree += 5f)
        {
            var degree = Mathf.Repeat(rawDegree, 360f);
            var deltaDegrees = Mathf.DeltaAngle(headingDegrees, degree);
            var x = centerX + deltaDegrees * pixelsPerDegree;
            if (x < left || x > left + width)
                continue;

            var roundedDegree = Mathf.RoundToInt(degree) % 360;
            var isCardinal = roundedDegree % 45 == 0;
            var isMajor = roundedDegree % 15 == 0;
            var edgeAlpha = CompassEdgeAlpha(x, left, width);
            DrawCompassTick(x, top, isCardinal, isMajor, edgeAlpha);

            if (isCardinal)
                DrawCompassLabel(x, top, CardinalLabel(roundedDegree), style, true, edgeAlpha);
            else if (isMajor)
                DrawCompassLabel(x, top, roundedDegree.ToString(), style, false, edgeAlpha);
        }
    }

    [HideFromIl2Cpp]
    private static void DrawCompassTick(
        float x,
        float top,
        bool isCardinal,
        bool isMajor,
        float alpha)
    {
        var tickHeight = isCardinal ? 15f : isMajor ? 10f : 6f;
        GUI.color = isCardinal
            ? new Color(1f, 0.84f, 0.25f, alpha)
            : new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(new Rect(x - 1f, top + 6f, 2f, tickHeight), Texture2D.whiteTexture);
    }

    [HideFromIl2Cpp]
    private static void DrawCompassLabel(
        float x,
        float top,
        string text,
        GUIStyle style,
        bool isCardinal,
        float alpha)
    {
        GUI.color = Color.white;
        style.normal.textColor = isCardinal
            ? new Color(1f, 0.84f, 0.25f, alpha)
            : new Color(1f, 1f, 1f, alpha);
        GUI.Label(new Rect(x - 28f, top + 22f, 56f, 22f), text, style);
    }

    [HideFromIl2Cpp]
    private static void DrawCompassBackground(float left, float top, float width, float height)
    {
        var fadeWidth = width * CompassEdgeFadeFraction;
        var middleWidth = Mathf.Max(0f, width - fadeWidth * 2f);
        GUI.color = new Color(0f, 0f, 0f, CompassBackgroundAlpha);
        GUI.DrawTexture(
            new Rect(left + fadeWidth, top, middleWidth, height),
            Texture2D.whiteTexture);

        var stripWidth = fadeWidth / CompassBackgroundFadeSteps;
        for (var step = 0; step < CompassBackgroundFadeSteps; step++)
        {
            var normalizedDistance = (step + 0.5f) / CompassBackgroundFadeSteps;
            var alpha = CompassBackgroundAlpha * Mathf.SmoothStep(0f, 1f, normalizedDistance);
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(
                new Rect(left + step * stripWidth, top, stripWidth + 0.5f, height),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(left + width - (step + 1f) * stripWidth, top, stripWidth + 0.5f, height),
                Texture2D.whiteTexture);
        }
    }

    [HideFromIl2Cpp]
    private static float CompassEdgeAlpha(float x, float left, float width)
    {
        var distanceFromEdge = Mathf.Min(x - left, left + width - x);
        var fadeWidth = width * CompassEdgeFadeFraction;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceFromEdge / fadeWidth));
    }

    [HideFromIl2Cpp]
    private void EnsureCompassStyle()
    {
        if (_compassLabelStyle != null)
            return;

        _compassLabelStyle = new GUIStyle();
        GUIStyle.Internal_Copy(_compassLabelStyle, GUI.skin.label);
        _compassLabelStyle.alignment = TextAnchor.UpperCenter;
        _compassLabelStyle.fontSize = 14;
        _compassLabelStyle.fontStyle = FontStyle.Bold;
    }

    [HideFromIl2Cpp]
    private static string CardinalLabel(int heading) => heading switch
    {
        0 => "N",
        45 => "NE",
        90 => "E",
        135 => "SE",
        180 => "S",
        225 => "SW",
        270 => "W",
        315 => "NW",
        _ => heading.ToString()
    };

    [HideFromIl2Cpp]
    private static void DrawBlackRect(float x, float y, float width, float height)
    {
        if (width <= 0f || height <= 0f)
            return;

        // The circular binocular mask produces fractional-pixel strip bounds.
        // IMGUI can drop a sub-pixel-wide outer strip, leaving isolated scene
        // pixels visible along the lens edge. Expand the blackout out to the
        // enclosing screen pixels so the mask remains continuous.
        var left = Mathf.Floor(x);
        var top = Mathf.Floor(y);
        var right = Mathf.Ceil(x + width);
        var bottom = Mathf.Ceil(y + height);
        if (right <= left || bottom <= top)
            return;

        GUI.DrawTexture(
            new Rect(left, top, right - left, bottom - top),
            Texture2D.whiteTexture);
    }

    [HideFromIl2Cpp]
    private void ResetViewFeatures()
    {
        _compassVisibleUntil = float.NegativeInfinity;
        _compassVisible = false;
        ResetOpticsAndFreeLook();
    }

    [HideFromIl2Cpp]
    private void ResetOpticsAndFreeLook()
    {
        SetBinocularsActive(false);
        _freeLookHeld = false;
        _freeLookPitch = 0f;
        _freeLookYaw = 0f;
        _lastRotationCaptureFrame = -1;
    }

    [HideFromIl2Cpp]
    private void SetBinocularsActive(bool active)
    {
        if (_binocularsActive == active)
            return;

        _binocularsActive = active;
        if (active)
        {
            var camera = ResourcesManager.mainCamera;
            if (camera != null)
            {
                _binocularCameraInstanceId = camera.GetInstanceID();
                _binocularBaseFov = Mathf.Clamp(camera.fieldOfView, 1f, 179f);
                camera.fieldOfView = GetBinocularFov(_binocularBaseFov);
            }

            HideCurrentViewModel();
            return;
        }

        var currentCamera = ResourcesManager.mainCamera;
        if (currentCamera != null &&
            currentCamera.GetInstanceID() == _binocularCameraInstanceId &&
            !float.IsNaN(_binocularBaseFov))
        {
            currentCamera.fieldOfView = _binocularBaseFov;
        }

        _binocularBaseFov = float.NaN;
        _binocularCameraInstanceId = 0;
        RestoreHiddenViewModel();
    }

    [HideFromIl2Cpp]
    private void HideCurrentViewModel()
    {
        var soldier = Soldier.CurrentControlledSoldierOrNull();
        var fpsGun = soldier?.GetFPSGunManager();
        if (fpsGun == null)
            return;

        if (_hiddenFpsGun != null &&
            _hiddenFpsGun.GetInstanceID() != fpsGun.GetInstanceID())
        {
            _hiddenFpsGun.HideFPSHandsAnim(false);
        }

        fpsGun.HideFPSHandsAnim(true);
        _hiddenFpsGun = fpsGun;
    }

    [HideFromIl2Cpp]
    private void RestoreHiddenViewModel()
    {
        if (_hiddenFpsGun != null)
            _hiddenFpsGun.HideFPSHandsAnim(false);

        _hiddenFpsGun = null;
    }

    [HideFromIl2Cpp]
    private void ReportError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastErrorSignature, StringComparison.Ordinal))
            return;

        _lastErrorSignature = signature;
        Plugin.LogSource.LogWarning(
            $"Player binocular/freelook controller failed (further identical errors suppressed): {exception.Message}");
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.GetCameraRotationInput))]
internal static class PlayerFreeLookInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Vector2 __result)
    {
        PlayerViewFeaturesController.CaptureAndSuppressRotation(ref __result);
    }
}

[HarmonyPatch(typeof(Soldier), nameof(Soldier.UpdateCameraPos))]
internal static class PlayerFreeLookCameraPatch
{
    [HarmonyPostfix]
    private static void Postfix(Soldier __instance)
    {
        PlayerViewFeaturesController.ApplyFreeLook(__instance);
    }
}

[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class PlayerBinocularCameraPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        var __t = ModTimeProbe.Begin();
        try
        {
            PlayerViewFeaturesController.ApplyBinocularView();
        }
        finally
        {
            ModTimeProbe.End(ModTimeSite.Other, __t);
        }
    }
}

[HarmonyPatch(typeof(MiniMapGUI), nameof(MiniMapGUI.OnEnable))]
internal static class PlayerCompassMapNorthPatch
{
    [HarmonyPostfix]
    private static void Postfix(MiniMapGUI __instance)
    {
        PlayerViewFeaturesController.CaptureMapNorthShift(__instance);
    }
}
