using System.Globalization;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ER2RealismOverhaul;

internal sealed class SettingsMenuController : MonoBehaviour
{
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;
    private const float TypographyScale = 1.3f;
    private const int MaximumSearchLength = 72;

    private static readonly Color BackdropColor = new(0.022f, 0.031f, 0.043f, 1f);
    private static readonly Color HeaderColor = new(0.035f, 0.052f, 0.069f, 1f);
    private static readonly Color PanelColor = new(0.047f, 0.068f, 0.088f, 1f);
    private static readonly Color RaisedColor = new(0.069f, 0.096f, 0.122f, 1f);
    private static readonly Color HoverColor = new(0.098f, 0.133f, 0.164f, 1f);
    private static readonly Color BorderColor = new(0.13f, 0.17f, 0.20f, 1f);
    private static readonly Color AccentColor = new(0.90f, 0.56f, 0.18f, 1f);
    private static readonly Color AccentDarkColor = new(0.36f, 0.22f, 0.075f, 1f);
    private static readonly Color SuccessColor = new(0.20f, 0.62f, 0.36f, 1f);
    private static readonly Color SuccessDarkColor = new(0.075f, 0.25f, 0.14f, 1f);
    private static readonly Color DangerColor = new(0.80f, 0.25f, 0.20f, 1f);
    private static readonly Color DisabledColor = new(0.052f, 0.065f, 0.076f, 1f);

    private readonly SettingsMenuCategory[] _categories = Enum.GetValues<SettingsMenuCategory>();

    private SettingsDraft? _draft;
    private SettingsMenuCategory _selectedCategory = SettingsMenuCategory.QuickSetup;
    private int _page;
    private int _visibleRows = 5;
    private string _search = string.Empty;
    private string _message = string.Empty;
    private string _lastRenderErrorSignature = string.Empty;
    private bool _searchFocused;
    private bool _open;
    private bool _pausedByMenu;
    private bool _previousCursorVisible;
    private bool _inputBlockErrorLogged;
    private bool _inputRestorePending;
    private bool _wheelInputAvailable = true;
    private CursorLockMode _previousCursorLock;
    private int _restoreInputAfterFrame;
    private readonly List<Behaviour> _blockedUiBehaviours = new();
    private readonly HashSet<int> _blockedUiBehaviourIds = new();

    private float _uiScale = 1f;
    private int _screenWidth;
    private int _screenHeight;
    private Rect _settingsViewport;
    private Vector2 _pointerPosition;
    private bool _pointerDown;

    private GUIStyle? _titleStyle;
    private GUIStyle? _headingStyle;
    private GUIStyle? _bodyStyle;
    private GUIStyle? _mutedStyle;
    private GUIStyle? _smallStyle;
    private GUIStyle? _descriptionStyle;
    private GUIStyle? _statusStyle;
    private GUIStyle? _errorStyle;
    private GUIStyle? _buttonStyle;
    private GUIStyle? _launcherStyle;
    private GUIStyle? _categoryStyle;
    private GUIStyle? _valueStyle;
    private GUIStyle? _directionLeftStyle;
    private GUIStyle? _directionRightStyle;
    private GUIStyle? _searchStyle;

    private void Update()
    {
        if (_inputRestorePending)
            BlockUnderlyingUi();
        TryRestoreUnderlyingUi();

        if (Input.GetKeyDown(KeyCode.F10))
        {
            if (_open)
                CloseMenu();
            else
                OpenMenu();
            return;
        }

        if (!_open)
            return;

        BlockUnderlyingUi();

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_searchFocused)
                _searchFocused = false;
            else
                CloseMenu();
            return;
        }

        UpdateSearchInput();

        if (Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.LeftArrow))
            ChangePage(-1);
        if (Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.RightArrow))
            ChangePage(1);
        if (Input.GetKeyDown(KeyCode.Home))
            SetPage(0);
        if (Input.GetKeyDown(KeyCode.End))
            SetPage(int.MaxValue);

        TryHandleMouseWheel();
    }

    private void OnGUI()
    {
        GUI.depth = -10000;
        var menuWasOpen = _open;

        try
        {
            if (_open)
                BlockUnderlyingUi();

            RefreshUiScale();
            EnsureStyles();
            RefreshPointerState();

            if (_open)
                DrawMenu();
            else
                DrawLauncher();
        }
        catch (Exception ex)
        {
            ReportRenderError(ex);
            if (_open)
                DrawEmergencyMenu();
        }
        finally
        {
            if (menuWasOpen)
                ConsumeCurrentGuiEvent();
        }
    }

    [HideFromIl2Cpp]
    private void DrawMenu()
    {
        FillRect(new Rect(0f, 0f, Screen.width, Screen.height), BackdropColor);

        var margin = Mathf.Max(S(18f), Mathf.Min(Screen.width, Screen.height) * 0.018f);
        var outer = new Rect(margin, margin, Screen.width - margin * 2f, Screen.height - margin * 2f);
        var gap = S(13f);
        var headerHeight = S(88f);
        var footerHeight = S(68f);
        var bodyY = outer.y + headerHeight + gap;
        var bodyHeight = Mathf.Max(S(350f), outer.height - headerHeight - footerHeight - gap * 2f);
        var body = new Rect(outer.x, bodyY, outer.width, bodyHeight);
        var footer = new Rect(outer.x, outer.yMax - footerHeight, outer.width, footerHeight);

        DrawHeader(new Rect(outer.x, outer.y, outer.width, headerHeight));
        if (!_open)
            return;

        var sidebarWidth = Mathf.Clamp(S(265f), S(190f), outer.width * 0.23f);
        var sidebar = new Rect(body.x, body.y, sidebarWidth, body.height);
        var content = new Rect(sidebar.xMax + gap, body.y, body.width - sidebarWidth - gap, body.height);

        DrawSidebar(sidebar);
        DrawContent(content);
        DrawFooter(footer);
    }

    [HideFromIl2Cpp]
    private void DrawHeader(Rect rect)
    {
        FillRect(rect, HeaderColor);
        FillRect(new Rect(rect.x, rect.y, S(6f), rect.height), AccentColor);
        DrawOutline(rect, BorderColor, S(1f));

        var padding = S(20f);
        var closeSize = S(42f);
        var closeRect = new Rect(rect.xMax - padding - closeSize, rect.y + S(17f), closeSize, closeSize);
        if (DrawButton(closeRect, "X", RaisedColor, DangerColor, BorderColor, _buttonStyle!))
            CloseMenu();

        var versionWidth = S(78f);
        var versionRect = new Rect(closeRect.x - S(10f) - versionWidth, closeRect.y, versionWidth, closeSize);
        FillRect(versionRect, RaisedColor);
        DrawOutline(versionRect, BorderColor, S(1f));
        GUI.Label(versionRect, $"v{Plugin.PluginVersion}", _valueStyle!);

        var titleRect = new Rect(rect.x + padding, rect.y + S(10f), rect.width * 0.52f, S(38f));
        GUI.Label(titleRect, "EASY RED 2  /  REALISM OVERHAUL", _titleStyle!);
        GUI.Label(
            new Rect(titleRect.x, titleRect.yMax, titleRect.width, S(25f)),
            "Tactical AI and battlefield configuration",
            _mutedStyle!);

        var statusWidth = Mathf.Max(S(280f), versionRect.x - S(18f) - (rect.x + rect.width * 0.54f));
        var statusRect = new Rect(rect.x + rect.width * 0.54f, rect.y + S(15f), statusWidth, S(52f));
        GUI.Label(statusRect, SettingsSyncController.StatusLabel, _statusStyle!);
    }

    [HideFromIl2Cpp]
    private void DrawSidebar(Rect rect)
    {
        FillRect(rect, PanelColor);
        DrawOutline(rect, BorderColor, S(1f));

        var padding = S(12f);
        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(10f), rect.width - padding * 2f, S(24f)),
            "CATEGORIES",
            _smallStyle!);

        var resetHeight = S(36f);
        var resetGap = S(7f);
        var resetAllRect = new Rect(
            rect.x + padding,
            rect.yMax - padding - resetHeight,
            rect.width - padding * 2f,
            resetHeight);
        var resetCategoryRect = new Rect(
            resetAllRect.x,
            resetAllRect.y - resetGap - resetHeight,
            resetAllRect.width,
            resetHeight);

        var categoryTop = rect.y + S(42f);
        var categoryBottom = resetCategoryRect.y - S(14f);
        var categoryGap = S(6f);
        var categoryHeight = (categoryBottom - categoryTop - categoryGap * (_categories.Length - 1)) / _categories.Length;
        categoryHeight = Mathf.Clamp(categoryHeight, S(31f), S(47f));

        for (var index = 0; index < _categories.Length; index++)
        {
            var category = _categories[index];
            var categoryRect = new Rect(
                rect.x + padding,
                categoryTop + index * (categoryHeight + categoryGap),
                rect.width - padding * 2f,
                categoryHeight);
            var selected = category == _selectedCategory;
            var count = CountSettings(category);
            var label = $"{SettingsCatalog.CategoryName(category)}   {count}";
            if (DrawButton(
                    categoryRect,
                    label,
                    selected ? AccentDarkColor : RaisedColor,
                    selected ? AccentColor : HoverColor,
                    selected ? AccentColor : BorderColor,
                    _categoryStyle!))
            {
                _selectedCategory = category;
                _page = 0;
                _searchFocused = false;
                _message = string.Empty;
            }
        }

        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !SettingsSyncController.AreSettingsReadOnly && _draft != null;
        if (DrawButton(resetCategoryRect, "RESET CATEGORY", RaisedColor, HoverColor, BorderColor, _buttonStyle!))
        {
            _draft!.ResetCategory(_selectedCategory);
            _message = "Category reset to compiled defaults. Apply to save.";
        }
        if (DrawButton(resetAllRect, "RESET EVERYTHING", RaisedColor, DangerColor, BorderColor, _buttonStyle!))
        {
            _draft!.ResetAll();
            _message = "Every setting reset to compiled defaults. Apply to save.";
        }
        GUI.enabled = wasEnabled;
    }

    [HideFromIl2Cpp]
    private void DrawContent(Rect rect)
    {
        FillRect(rect, PanelColor);
        DrawOutline(rect, BorderColor, S(1f));

        var padding = S(12f);
        var headerHeight = S(86f);
        var titleWidth = rect.width * 0.42f;
        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(8f), titleWidth, S(32f)),
            SettingsCatalog.CategoryName(_selectedCategory),
            _headingStyle!);

        var visibleCount = CountVisibleSettings();
        var totalCount = CountSettings(_selectedCategory);
        var countText = string.IsNullOrWhiteSpace(_search)
            ? $"{visibleCount} settings"
            : $"{visibleCount} of {totalCount} settings match";
        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(40f), titleWidth, S(22f)),
            countText,
            _mutedStyle!);

        DrawSearch(rect, padding);

        var readOnlyHeight = SettingsSyncController.AreSettingsReadOnly ? S(38f) : 0f;
        if (SettingsSyncController.AreSettingsReadOnly)
        {
            var readOnlyRect = new Rect(
                rect.x + padding,
                rect.y + headerHeight,
                rect.width - padding * 2f,
                readOnlyHeight - S(6f));
            FillRect(readOnlyRect, AccentDarkColor);
            DrawOutline(readOnlyRect, AccentColor, S(1f));
            GUI.Label(
                new Rect(readOnlyRect.x + S(10f), readOnlyRect.y, readOnlyRect.width - S(20f), readOnlyRect.height),
                "READ ONLY  /  The multiplayer host controls this session.",
                _statusStyle!);
        }

        _settingsViewport = new Rect(
            rect.x + padding,
            rect.y + headerHeight + readOnlyHeight,
            rect.width - padding * 2f,
            rect.height - headerHeight - readOnlyHeight - padding);

        var cardGap = S(8f);
        var desiredCardHeight = S(98f);
        _visibleRows = Mathf.Clamp(
            Mathf.FloorToInt((_settingsViewport.height + cardGap) / (desiredCardHeight + cardGap)),
            4,
            8);

        var pageCount = PageCount(visibleCount);
        _page = Mathf.Clamp(_page, 0, pageCount - 1);
        DrawPager(rect, padding, pageCount, visibleCount);

        if (visibleCount == 0)
        {
            FillRect(_settingsViewport, RaisedColor);
            DrawOutline(_settingsViewport, BorderColor, S(1f));
            GUI.Label(
                new Rect(
                    _settingsViewport.x + S(20f),
                    _settingsViewport.y + S(24f),
                    _settingsViewport.width - S(40f),
                    S(80f)),
                "No settings match this search. Clear the filter or choose another category.",
                _headingStyle!);
            return;
        }

        var cardHeight = (_settingsViewport.height - cardGap * (_visibleRows - 1)) / _visibleRows;
        var firstVisible = _page * _visibleRows;
        var matchedIndex = 0;
        var drawn = 0;

        foreach (var setting in SettingsCatalog.All)
        {
            if (!IsVisible(setting))
                continue;
            if (matchedIndex++ < firstVisible)
                continue;

            var card = new Rect(
                _settingsViewport.x,
                _settingsViewport.y + drawn * (cardHeight + cardGap),
                _settingsViewport.width,
                cardHeight);
            DrawSettingCard(setting, card);
            drawn++;
            if (drawn >= _visibleRows)
                break;
        }

        DrawPageProgress(_settingsViewport, visibleCount, pageCount);
    }

    [HideFromIl2Cpp]
    private void DrawSearch(Rect contentRect, float padding)
    {
        var clearWidth = S(66f);
        var searchWidth = Mathf.Clamp(S(330f), S(215f), contentRect.width * 0.39f);
        var searchRect = new Rect(
            contentRect.xMax - padding - searchWidth,
            contentRect.y + S(10f),
            searchWidth,
            S(34f));
        var clearRect = new Rect(
            searchRect.xMax - clearWidth,
            searchRect.y,
            clearWidth,
            searchRect.height);
        var fieldRect = new Rect(searchRect.x, searchRect.y, searchRect.width - clearWidth - S(6f), searchRect.height);

        FillRect(fieldRect, _searchFocused ? HoverColor : RaisedColor);
        DrawOutline(fieldRect, _searchFocused ? AccentColor : BorderColor, S(1f));
        var display = string.IsNullOrEmpty(_search)
            ? "Type to filter settings..."
            : _search + (_searchFocused ? "|" : string.Empty);
        GUI.Label(
            new Rect(fieldRect.x + S(10f), fieldRect.y, fieldRect.width - S(20f), fieldRect.height),
            display,
            string.IsNullOrEmpty(_search) ? _mutedStyle! : _searchStyle!);
        if (GUI.Button(fieldRect, string.Empty, _buttonStyle!))
            _searchFocused = true;

        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !string.IsNullOrEmpty(_search);
        if (DrawButton(clearRect, "CLEAR", RaisedColor, HoverColor, BorderColor, _buttonStyle!))
        {
            _search = string.Empty;
            _page = 0;
            _searchFocused = true;
        }
        GUI.enabled = wasEnabled;
    }

    [HideFromIl2Cpp]
    private void DrawPager(Rect contentRect, float padding, int pageCount, int visibleCount)
    {
        var buttonSize = S(34f);
        var pageWidth = S(118f);
        var nextRect = new Rect(
            contentRect.xMax - padding - buttonSize,
            contentRect.y + S(48f),
            buttonSize,
            buttonSize);
        var pageRect = new Rect(nextRect.x - S(6f) - pageWidth, nextRect.y, pageWidth, buttonSize);
        var previousRect = new Rect(pageRect.x - S(6f) - buttonSize, nextRect.y, buttonSize, buttonSize);

        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && visibleCount > 0 && _page > 0;
        if (DrawButton(previousRect, "<", RaisedColor, HoverColor, BorderColor, _buttonStyle!))
        {
            ChangePage(-1);
            _searchFocused = false;
        }
        GUI.enabled = wasEnabled;

        FillRect(pageRect, HeaderColor);
        DrawOutline(pageRect, BorderColor, S(1f));
        GUI.Label(pageRect, $"PAGE {_page + 1} / {pageCount}", _valueStyle!);

        GUI.enabled = wasEnabled && visibleCount > 0 && _page < pageCount - 1;
        if (DrawButton(nextRect, ">", RaisedColor, HoverColor, BorderColor, _buttonStyle!))
        {
            ChangePage(1);
            _searchFocused = false;
        }
        GUI.enabled = wasEnabled;
    }

    [HideFromIl2Cpp]
    private void DrawSettingCard(MenuSetting setting, Rect rect)
    {
        if (_draft == null)
            return;

        var changed = !string.Equals(
            _draft.Get(setting),
            setting.Entry.GetSerializedValue(),
            StringComparison.Ordinal);
        FillRect(rect, changed ? new Color(0.075f, 0.087f, 0.094f, 1f) : RaisedColor);
        DrawOutline(rect, changed ? AccentColor : BorderColor, changed ? S(2f) : S(1f));
        if (changed)
            FillRect(new Rect(rect.x, rect.y, S(5f), rect.height), AccentColor);

        var padding = S(13f);
        var resetWidth = S(64f);
        var resetGap = S(10f);
        var controlWidth = Mathf.Clamp(S(330f), S(250f), rect.width * 0.36f);
        var resetRect = new Rect(
            rect.xMax - padding - resetWidth,
            rect.y + (rect.height - S(32f)) * 0.5f,
            resetWidth,
            S(32f));
        var controlRect = new Rect(
            resetRect.x - resetGap - controlWidth,
            rect.y + S(6f),
            controlWidth,
            rect.height - S(12f));
        var textWidth = Mathf.Max(S(180f), controlRect.x - (rect.x + padding) - S(14f));

        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(6f), textWidth, S(17f)),
            setting.SectionName.ToUpperInvariant(),
            _smallStyle!);
        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(23f), textWidth, S(26f)),
            setting.DisplayName,
            _bodyStyle!);
        if (!string.IsNullOrWhiteSpace(setting.Description))
        {
            GUI.Label(
                new Rect(
                    rect.x + padding,
                    rect.y + S(49f),
                    textWidth,
                    Mathf.Max(S(18f), rect.height - S(54f))),
                setting.Description,
                _descriptionStyle!);
        }

        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !SettingsSyncController.AreSettingsReadOnly;
        if (setting.Entry.SettingType == typeof(bool))
            DrawBooleanControl(setting, controlRect);
        else
            DrawNumberControl(setting, controlRect);

        if (DrawButton(resetRect, "RESET", PanelColor, HoverColor, BorderColor, _buttonStyle!))
            _draft.Reset(setting);
        GUI.enabled = wasEnabled;
    }

    [HideFromIl2Cpp]
    private void DrawBooleanControl(MenuSetting setting, Rect rect)
    {
        if (_draft == null)
            return;

        var current = bool.TryParse(_draft.Get(setting), out var parsed) && parsed;
        var width = Mathf.Min(S(150f), rect.width);
        var toggleRect = new Rect(
            rect.xMax - width,
            rect.y + (rect.height - S(38f)) * 0.5f,
            width,
            S(38f));
        if (DrawButton(
                toggleRect,
                current ? "ENABLED" : "DISABLED",
                current ? SuccessDarkColor : PanelColor,
                current ? SuccessColor : HoverColor,
                current ? SuccessColor : BorderColor,
                _buttonStyle!))
        {
            _draft.Set(setting, current ? "false" : "true");
        }
    }

    [HideFromIl2Cpp]
    private void DrawNumberControl(MenuSetting setting, Rect rect)
    {
        if (_draft == null)
            return;

        var text = _draft.Get(setting);
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            current = 0f;

        var hasRange = TryGetRange(setting.Entry, out var minimum, out var maximum);
        var isInteger = setting.Entry.SettingType == typeof(int);
        var step = isInteger
            ? Mathf.Max(1f, hasRange ? Mathf.Round((maximum - minimum) / 100f) : 1f)
            : hasRange
                ? Mathf.Max(0.001f, (maximum - minimum) / 100f)
                : Mathf.Max(0.1f, Mathf.Abs(current) * 0.1f);

        var buttonSize = S(31f);
        var valueWidth = S(92f);
        var gap = S(6f);
        var controlsWidth = buttonSize * 2f + valueWidth + gap * 2f;
        var controlsX = rect.xMax - controlsWidth;
        var controlsY = rect.y + S(4f);
        var minusRect = new Rect(controlsX, controlsY, buttonSize, buttonSize);
        var valueRect = new Rect(minusRect.xMax + gap, controlsY, valueWidth, buttonSize);
        var plusRect = new Rect(valueRect.xMax + gap, controlsY, buttonSize, buttonSize);

        var changed = false;
        if (DrawButton(minusRect, "-", PanelColor, HoverColor, BorderColor, _buttonStyle!))
        {
            current -= step;
            changed = true;
        }

        FillRect(valueRect, HeaderColor);
        DrawOutline(valueRect, BorderColor, S(1f));
        var valueText = FormatRangeValue(current, setting.Entry.SettingType);
        if (!string.IsNullOrEmpty(setting.Unit) && setting.Unit != "0-1")
            valueText += " " + setting.Unit;
        GUI.Label(valueRect, valueText, _valueStyle!);

        if (DrawButton(plusRect, "+", PanelColor, HoverColor, BorderColor, _buttonStyle!))
        {
            current += step;
            changed = true;
        }

        if (hasRange)
        {
            var sliderRect = new Rect(rect.x, rect.yMax - S(25f), rect.width, S(20f));
            var hintTop = rect.y + S(38f);
            var hintBottom = sliderRect.y - S(2f);
            if (hintBottom > hintTop)
            {
                var hintGap = S(8f);
                var hintWidth = (rect.width - hintGap) * 0.5f;
                var hintHeight = hintBottom - hintTop;
                GUI.Label(
                    new Rect(rect.x, hintTop, hintWidth, hintHeight),
                    "LOWER\n" + setting.LowerEffect,
                    _directionLeftStyle!);
                GUI.Label(
                    new Rect(rect.xMax - hintWidth, hintTop, hintWidth, hintHeight),
                    "HIGHER\n" + setting.HigherEffect,
                    _directionRightStyle!);
            }

            var oldColor = GUI.color;
            GUI.color = AccentColor;
            var sliderValue = GUI.HorizontalSlider(sliderRect, current, minimum, maximum);
            GUI.color = oldColor;
            if (isInteger)
                sliderValue = Mathf.Round(sliderValue);
            if (!Mathf.Approximately(sliderValue, current))
            {
                current = sliderValue;
                changed = true;
            }

            var rangeText = $"{FormatRangeValue(minimum, setting.Entry.SettingType)}  -  {FormatRangeValue(maximum, setting.Entry.SettingType)}";
            GUI.Label(
                new Rect(rect.x, controlsY, Mathf.Max(S(70f), controlsX - rect.x - S(8f)), buttonSize),
                rangeText,
                _mutedStyle!);
        }

        if (!changed)
            return;

        if (hasRange)
            current = Mathf.Clamp(current, minimum, maximum);
        if (isInteger)
            current = Mathf.Round(current);
        _draft.Set(setting, FormatRangeValue(current, setting.Entry.SettingType));
    }

    [HideFromIl2Cpp]
    private void DrawFooter(Rect rect)
    {
        FillRect(rect, HeaderColor);
        DrawOutline(rect, BorderColor, S(1f));

        var padding = S(12f);
        var buttonHeight = S(40f);
        var cancelWidth = S(108f);
        var exportWidth = S(148f);
        var applyWidth = S(116f);
        var gap = S(8f);
        var cancelRect = new Rect(
            rect.xMax - padding - cancelWidth,
            rect.y + (rect.height - buttonHeight) * 0.5f,
            cancelWidth,
            buttonHeight);
        var exportRect = new Rect(
            cancelRect.x - gap - exportWidth,
            cancelRect.y,
            exportWidth,
            buttonHeight);
        var applyRect = new Rect(
            exportRect.x - gap - applyWidth,
            cancelRect.y,
            applyWidth,
            buttonHeight);

        var dirty = _draft?.IsDirty == true;
        var stateText = dirty ? "UNSAVED CHANGES" : "ALL CHANGES SAVED";
        var stateColor = dirty ? AccentColor : SuccessColor;
        GUI.Label(
            new Rect(rect.x + padding, rect.y + S(8f), S(190f), S(22f)),
            stateText,
            dirty ? _errorStyle! : _statusStyle!);

        var messageRect = new Rect(
            rect.x + padding,
            rect.y + S(31f),
            Mathf.Max(S(200f), applyRect.x - rect.x - padding * 2f),
            S(26f));
        var message = string.IsNullOrWhiteSpace(_message)
            ? "Mouse wheel or Page Up/Down changes pages. F10 or Esc closes the menu."
            : _message;
        GUI.Label(
            messageRect,
            message,
            _message.StartsWith("Error", StringComparison.Ordinal) ? _errorStyle! : _mutedStyle!);
        FillRect(new Rect(rect.x + padding, rect.y + S(6f), S(4f), S(20f)), stateColor);

        var wasEnabled = GUI.enabled;
        GUI.enabled = wasEnabled && !SettingsSyncController.AreSettingsReadOnly && dirty;
        if (DrawButton(applyRect, "APPLY", AccentDarkColor, AccentColor, AccentColor, _buttonStyle!))
            ApplyDraft();
        GUI.enabled = wasEnabled && !SettingsSyncController.AreSettingsReadOnly && _draft != null;
        if (DrawButton(exportRect, "EXPORT TUNING", RaisedColor, HoverColor, BorderColor, _buttonStyle!))
            ExportDraft();
        GUI.enabled = wasEnabled;
        if (DrawButton(cancelRect, "CANCEL", RaisedColor, DangerColor, BorderColor, _buttonStyle!))
            CloseMenu();
    }

    [HideFromIl2Cpp]
    private void DrawPageProgress(Rect viewport, int visibleCount, int pageCount)
    {
        if (pageCount <= 1 || visibleCount <= 0)
            return;

        var trackWidth = S(4f);
        var track = new Rect(viewport.xMax - trackWidth, viewport.y, trackWidth, viewport.height);
        FillRect(track, HeaderColor);
        var thumbHeight = Mathf.Max(S(22f), track.height / pageCount);
        var travel = track.height - thumbHeight;
        var thumbY = pageCount <= 1 ? track.y : track.y + travel * (_page / (float)(pageCount - 1));
        FillRect(new Rect(track.x, thumbY, track.width, thumbHeight), AccentColor);
    }

    [HideFromIl2Cpp]
    private void ApplyDraft()
    {
        if (_draft == null)
            return;

        if (_draft.Apply(out var error))
        {
            _message = SettingsSyncController.ShouldPersistAppliedSettings
                ? "Settings applied and saved."
                : "Session settings applied.";
        }
        else
        {
            _message = "Error: " + error;
        }
    }

    [HideFromIl2Cpp]
    private void ExportDraft()
    {
        if (_draft == null)
            return;

        if (!SettingsExport.TryWrite(_draft, out var exportPath, out var changedValueCount, out var error))
        {
            _message = "Error: " + error;
            return;
        }

        Plugin.LogSource.LogInfo($"Exported the current settings draft to {exportPath}");
        try
        {
            GUIUtility.systemCopyBuffer = exportPath;
            _message = $"Exported {changedValueCount} changed defaults. File path copied to clipboard.";
        }
        catch (Exception ex)
        {
            _message = $"Exported {changedValueCount} changed defaults. See the log for the file path.";
            Plugin.LogSource.LogDebug($"Could not copy the tuning export path to the clipboard: {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void DrawLauncher()
    {
        if (!Settings.ShowSettingsLauncherButton.Value || !ShouldShowLauncher())
            return;

        var width = S(330f);
        var height = S(52f);
        var edge = S(18f);
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - edge, width, height);
        var clicked = DrawButton(
            rect,
            "REALISM OVERHAUL SETTINGS  [F10]",
            HeaderColor,
            HoverColor,
            AccentDarkColor,
            _launcherStyle!);
        FillRect(new Rect(rect.x, rect.y, S(5f), rect.height), AccentColor);
        if (clicked)
            OpenMenu();
    }

    [HideFromIl2Cpp]
    private void DrawEmergencyMenu()
    {
        try
        {
            FillRect(new Rect(0f, 0f, Screen.width, Screen.height), BackdropColor);
            GUI.Label(
                new Rect(S(40f), S(40f), Screen.width - S(80f), S(80f)),
                "The settings menu hit a rendering error. The error was logged once to BepInEx.",
                GUI.skin.label);
            if (GUI.Button(new Rect(S(40f), S(130f), S(160f), S(44f)), "Close settings"))
                CloseMenu();
        }
        catch
        {
            // Never let a fallback renderer create another per-frame exception flood.
        }
    }

    [HideFromIl2Cpp]
    private void UpdateSearchInput()
    {
        if (!_searchFocused)
            return;

        var changed = false;
        var handledBackspace = false;
        var input = Input.inputString;
        foreach (var character in input)
        {
            if (character == '\b')
            {
                handledBackspace = true;
                if (_search.Length > 0)
                {
                    _search = _search[..^1];
                    changed = true;
                }
                continue;
            }

            if (character == '\n' || character == '\r')
            {
                _searchFocused = false;
                continue;
            }

            if (!char.IsControl(character) && _search.Length < MaximumSearchLength)
            {
                _search += character;
                changed = true;
            }
        }

        if (!handledBackspace && Input.GetKeyDown(KeyCode.Backspace) && _search.Length > 0)
        {
            _search = _search[..^1];
            changed = true;
        }

        var controlDown = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (controlDown && Input.GetKeyDown(KeyCode.A))
        {
            _search = string.Empty;
            changed = true;
        }
        else if (controlDown && Input.GetKeyDown(KeyCode.V))
        {
            try
            {
                var clipboard = GUIUtility.systemCopyBuffer ?? string.Empty;
                if (!string.IsNullOrEmpty(clipboard))
                {
                    foreach (var character in clipboard)
                    {
                        if (_search.Length >= MaximumSearchLength)
                            break;
                        if (!char.IsControl(character))
                            _search += character;
                    }
                    changed = true;
                }
            }
            catch
            {
                // Clipboard input is optional; typed filtering remains available.
            }
        }

        if (changed)
            _page = 0;
    }

    [HideFromIl2Cpp]
    private void TryHandleMouseWheel()
    {
        if (!_wheelInputAvailable || _settingsViewport.width <= 0f)
            return;

        var pointer = GetGuiPointerPosition();
        if (!Contains(_settingsViewport, pointer))
            return;

        try
        {
            var delta = Input.mouseScrollDelta.y;
            if (delta > 0.01f)
                ChangePage(-1);
            else if (delta < -0.01f)
                ChangePage(1);
        }
        catch (Exception ex)
        {
            _wheelInputAvailable = false;
            Plugin.LogSource.LogDebug($"Mouse-wheel paging is unavailable on this game build: {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void ChangePage(int delta)
    {
        SetPage(_page + delta);
    }

    [HideFromIl2Cpp]
    private void SetPage(int page)
    {
        var pageCount = PageCount(CountVisibleSettings());
        _page = Mathf.Clamp(page, 0, pageCount - 1);
    }

    [HideFromIl2Cpp]
    private int PageCount(int visibleCount)
    {
        return Mathf.Max(1, Mathf.CeilToInt(visibleCount / (float)Mathf.Max(1, _visibleRows)));
    }

    [HideFromIl2Cpp]
    private int CountVisibleSettings()
    {
        var count = 0;
        foreach (var setting in SettingsCatalog.All)
        {
            if (IsVisible(setting))
                count++;
        }
        return count;
    }

    [HideFromIl2Cpp]
    private int CountSettings(SettingsMenuCategory category)
    {
        var count = 0;
        foreach (var setting in SettingsCatalog.All)
        {
            if (SettingsDraft.IsInCategory(setting, category))
                count++;
        }
        return count;
    }

    [HideFromIl2Cpp]
    private bool IsVisible(MenuSetting setting)
    {
        return SettingsDraft.IsInCategory(setting, _selectedCategory) && setting.Matches(_search);
    }

    [HideFromIl2Cpp]
    private void OpenMenu()
    {
        if (_open)
            return;

        _draft = new SettingsDraft();
        _page = 0;
        _message = string.Empty;
        _lastRenderErrorSignature = string.Empty;
        _searchFocused = false;
        _open = true;
        _inputRestorePending = false;
        _previousCursorVisible = Cursor.visible;
        _previousCursorLock = Cursor.lockState;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        try
        {
            if (Pause.instance != null && !Pause.IsPaused())
            {
                Pause.SetPause(true);
                _pausedByMenu = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not open the game's pause layer behind the settings menu: {ex.Message}");
        }

        BlockUnderlyingUi();
    }

    [HideFromIl2Cpp]
    private void CloseMenu()
    {
        if (!_open)
            return;

        _open = false;
        _searchFocused = false;
        _draft = null;
        _message = string.Empty;
        ScheduleUnderlyingUiRestore();

        try
        {
            if (_pausedByMenu && Pause.instance != null && Pause.IsPaused())
                Pause.SetPause(false);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogDebug($"Could not restore the pause state after closing settings: {ex.Message}");
        }
        finally
        {
            // Pause.SetPause(false) may re-enable or replace the game's uGUI
            // components. Catch those replacements before this mouse-up can
            // reach the resumed interface.
            if (_inputRestorePending)
                BlockUnderlyingUi();
            _pausedByMenu = false;
            Cursor.visible = _previousCursorVisible;
            Cursor.lockState = _previousCursorLock;
        }
    }

    private void OnDestroy()
    {
        if (_open)
            CloseMenu();

        RestoreUnderlyingUiNow();
    }

    [HideFromIl2Cpp]
    private static void ConsumeCurrentGuiEvent()
    {
        var current = Event.current;
        if (current == null)
            return;

        switch (current.type)
        {
            case EventType.MouseDown:
            case EventType.MouseUp:
            case EventType.MouseDrag:
            case EventType.ScrollWheel:
            case EventType.KeyDown:
            case EventType.KeyUp:
                current.Use();
                break;
        }
    }

    [HideFromIl2Cpp]
    private void BlockUnderlyingUi()
    {
        try
        {
            // Easy Red 2 may replace EventSystem.current or activate another input
            // path when its pause UI changes state. Disable every active part of
            // the uGUI event pipeline, including components created after opening.
            foreach (var eventSystem in UnityEngine.Object.FindObjectsOfType<EventSystem>())
                BlockUiBehaviour(eventSystem);
            foreach (var inputModule in UnityEngine.Object.FindObjectsOfType<BaseInputModule>())
                BlockUiBehaviour(inputModule);
            foreach (var raycaster in UnityEngine.Object.FindObjectsOfType<BaseRaycaster>())
                BlockUiBehaviour(raycaster);
        }
        catch (Exception ex)
        {
            if (_inputBlockErrorLogged)
                return;
            _inputBlockErrorLogged = true;
            Plugin.LogSource.LogWarning($"Could not block the game's UI while settings are open: {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void BlockUiBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
            return;

        var id = behaviour.GetInstanceID();
        if (_blockedUiBehaviourIds.Add(id))
            _blockedUiBehaviours.Add(behaviour);
        behaviour.enabled = false;
    }

    [HideFromIl2Cpp]
    private void ScheduleUnderlyingUiRestore()
    {
        // Restoring a uGUI EventSystem during the IMGUI mouse-up that closed the
        // overlay lets the underlying pause menu interpret that release as its
        // own click. Keep input blocked through the following frame instead.
        _inputRestorePending = true;
        _restoreInputAfterFrame = Time.frameCount + 2;
    }

    [HideFromIl2Cpp]
    private void TryRestoreUnderlyingUi()
    {
        if (!_inputRestorePending || _open || Time.frameCount < _restoreInputAfterFrame ||
            Input.GetMouseButton(0))
        {
            return;
        }

        RestoreUnderlyingUiNow();
    }

    [HideFromIl2Cpp]
    private void RestoreUnderlyingUiNow()
    {
        _inputRestorePending = false;
        _inputBlockErrorLogged = false;

        foreach (var behaviour in _blockedUiBehaviours)
        {
            try
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogDebug($"Could not restore a game UI input component after closing settings: {ex.Message}");
            }
        }

        _blockedUiBehaviours.Clear();
        _blockedUiBehaviourIds.Clear();
    }

    [HideFromIl2Cpp]
    private static bool ShouldShowLauncher()
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
    private static bool TryGetRange(ConfigEntryBase entry, out float minimum, out float maximum)
    {
        if (entry.Description.AcceptableValues is AcceptableValueRange<float> floatRange)
        {
            minimum = floatRange.MinValue;
            maximum = floatRange.MaxValue;
            return true;
        }

        if (entry.Description.AcceptableValues is AcceptableValueRange<int> intRange)
        {
            minimum = intRange.MinValue;
            maximum = intRange.MaxValue;
            return true;
        }

        minimum = 0f;
        maximum = 1f;
        return false;
    }

    [HideFromIl2Cpp]
    private static string FormatRangeValue(float value, Type settingType)
    {
        return settingType == typeof(int)
            ? Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    [HideFromIl2Cpp]
    private bool DrawButton(
        Rect rect,
        string text,
        Color normal,
        Color hover,
        Color border,
        GUIStyle style)
    {
        var enabled = GUI.enabled;
        var hovered = enabled && Contains(rect, _pointerPosition);
        var fill = enabled ? (hovered ? hover : normal) : DisabledColor;
        if (hovered && _pointerDown)
            fill = normal;
        FillRect(rect, fill);
        DrawOutline(rect, enabled ? border : BorderColor, S(1f));

        // ER2's built-in button states tint custom text almost black. Keep the
        // native button as the hit target, then paint the caption as a label so
        // every state remains legible (including disabled multiplayer controls).
        var clicked = GUI.Button(rect, string.Empty, style);
        GUI.enabled = true;
        GUI.Label(rect, text, style);
        GUI.enabled = enabled;
        return enabled && clicked;
    }

    [HideFromIl2Cpp]
    private static void FillRect(Rect rect, Color color)
    {
        var oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
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
    private void RefreshPointerState()
    {
        _pointerPosition = GetGuiPointerPosition();
        _pointerDown = Input.GetMouseButton(0);
    }

    [HideFromIl2Cpp]
    private static Vector2 GetGuiPointerPosition()
    {
        var pointer = Input.mousePosition;
        return new Vector2(pointer.x, Screen.height - pointer.y);
    }

    [HideFromIl2Cpp]
    private static bool Contains(Rect rect, Vector2 point)
    {
        return point.x >= rect.x && point.x <= rect.xMax && point.y >= rect.y && point.y <= rect.yMax;
    }

    [HideFromIl2Cpp]
    private void RefreshUiScale()
    {
        var nextScale = Mathf.Clamp(
            Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight),
            0.78f,
            2.5f);
        if (_screenWidth == Screen.width &&
            _screenHeight == Screen.height &&
            Mathf.Abs(nextScale - _uiScale) < 0.005f)
        {
            return;
        }

        _screenWidth = Screen.width;
        _screenHeight = Screen.height;
        _uiScale = nextScale;
        InvalidateStyles();
    }

    [HideFromIl2Cpp]
    private float S(float pixels) => pixels * _uiScale;

    [HideFromIl2Cpp]
    private int FontSize(float points) =>
        Mathf.Max(10, Mathf.RoundToInt(points * _uiScale * TypographyScale));

    [HideFromIl2Cpp]
    private void EnsureStyles()
    {
        if (_titleStyle != null)
            return;

        _titleStyle = CloneStyle(GUI.skin.label);
        _titleStyle.fontSize = FontSize(27f);
        _titleStyle.fontStyle = FontStyle.Bold;
        _titleStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_titleStyle, new Color(0.96f, 0.97f, 0.98f, 1f));

        _headingStyle = CloneStyle(GUI.skin.label);
        _headingStyle.fontSize = FontSize(22f);
        _headingStyle.fontStyle = FontStyle.Bold;
        _headingStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_headingStyle, new Color(0.94f, 0.95f, 0.96f, 1f));

        _bodyStyle = CloneStyle(GUI.skin.label);
        _bodyStyle.fontSize = FontSize(16f);
        _bodyStyle.fontStyle = FontStyle.Bold;
        _bodyStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_bodyStyle, new Color(0.93f, 0.94f, 0.95f, 1f));

        _mutedStyle = CloneStyle(GUI.skin.label);
        _mutedStyle.fontSize = FontSize(13f);
        _mutedStyle.alignment = TextAnchor.MiddleLeft;
        _mutedStyle.wordWrap = true;
        SetTextColors(_mutedStyle, new Color(0.62f, 0.68f, 0.72f, 1f));

        _smallStyle = CloneStyle(GUI.skin.label);
        _smallStyle.fontSize = FontSize(11f);
        _smallStyle.fontStyle = FontStyle.Bold;
        _smallStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_smallStyle, AccentColor);

        _descriptionStyle = CloneStyle(GUI.skin.label);
        _descriptionStyle.fontSize = FontSize(12f);
        _descriptionStyle.alignment = TextAnchor.UpperLeft;
        _descriptionStyle.wordWrap = true;
        SetTextColors(_descriptionStyle, new Color(0.68f, 0.72f, 0.75f, 1f));

        _statusStyle = CloneStyle(GUI.skin.label);
        _statusStyle.fontSize = FontSize(12f);
        _statusStyle.fontStyle = FontStyle.Bold;
        _statusStyle.alignment = TextAnchor.MiddleLeft;
        _statusStyle.wordWrap = true;
        SetTextColors(_statusStyle, new Color(0.42f, 0.82f, 0.55f, 1f));

        _errorStyle = CloneStyle(GUI.skin.label);
        _errorStyle.fontSize = FontSize(14f);
        _errorStyle.fontStyle = FontStyle.Bold;
        _errorStyle.alignment = TextAnchor.MiddleLeft;
        _errorStyle.wordWrap = true;
        SetTextColors(_errorStyle, new Color(1f, 0.58f, 0.30f, 1f));

        _buttonStyle = CloneStyle(GUI.skin.label);
        _buttonStyle.fontSize = FontSize(13f);
        _buttonStyle.fontStyle = FontStyle.Bold;
        _buttonStyle.alignment = TextAnchor.MiddleCenter;
        SetTextColors(_buttonStyle, new Color(0.94f, 0.95f, 0.96f, 1f));

        // The compact main/pause-menu launcher scales with the screen, but does
        // not inherit the enlarged typography used inside the full F10 menu.
        _launcherStyle = CloneStyle(GUI.skin.label);
        _launcherStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(13f * _uiScale));
        _launcherStyle.fontStyle = FontStyle.Bold;
        _launcherStyle.alignment = TextAnchor.MiddleCenter;
        SetTextColors(_launcherStyle, new Color(0.94f, 0.95f, 0.96f, 1f));

        _categoryStyle = CloneStyle(GUI.skin.label);
        _categoryStyle.fontSize = FontSize(14f);
        _categoryStyle.fontStyle = FontStyle.Bold;
        _categoryStyle.alignment = TextAnchor.MiddleLeft;
        _categoryStyle.padding = new RectOffset
        {
            left = Mathf.RoundToInt(S(12f)),
            right = Mathf.RoundToInt(S(8f)),
            top = 0,
            bottom = 0
        };
        SetTextColors(_categoryStyle, new Color(0.94f, 0.95f, 0.96f, 1f));

        _valueStyle = CloneStyle(GUI.skin.label);
        _valueStyle.fontSize = FontSize(14f);
        _valueStyle.fontStyle = FontStyle.Bold;
        _valueStyle.alignment = TextAnchor.MiddleCenter;
        SetTextColors(_valueStyle, new Color(0.94f, 0.95f, 0.96f, 1f));

        _directionLeftStyle = CloneStyle(GUI.skin.label);
        _directionLeftStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(11f * _uiScale));
        _directionLeftStyle.fontStyle = FontStyle.Bold;
        _directionLeftStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_directionLeftStyle, new Color(0.68f, 0.78f, 0.84f, 1f));

        _directionRightStyle = CloneStyle(GUI.skin.label);
        _directionRightStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(11f * _uiScale));
        _directionRightStyle.fontStyle = FontStyle.Bold;
        _directionRightStyle.alignment = TextAnchor.MiddleRight;
        SetTextColors(_directionRightStyle, new Color(0.68f, 0.78f, 0.84f, 1f));

        _searchStyle = CloneStyle(GUI.skin.label);
        _searchStyle.fontSize = FontSize(15f);
        _searchStyle.alignment = TextAnchor.MiddleLeft;
        SetTextColors(_searchStyle, new Color(0.90f, 0.92f, 0.94f, 1f));
    }

    [HideFromIl2Cpp]
    private void InvalidateStyles()
    {
        _titleStyle = null;
        _headingStyle = null;
        _bodyStyle = null;
        _mutedStyle = null;
        _smallStyle = null;
        _descriptionStyle = null;
        _statusStyle = null;
        _errorStyle = null;
        _buttonStyle = null;
        _launcherStyle = null;
        _categoryStyle = null;
        _valueStyle = null;
        _directionLeftStyle = null;
        _directionRightStyle = null;
        _searchStyle = null;
    }

    [HideFromIl2Cpp]
    private static void SetTextColors(GUIStyle style, Color color)
    {
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
    }

    [HideFromIl2Cpp]
    private static GUIStyle CloneStyle(GUIStyle source)
    {
        var style = new GUIStyle();
        GUIStyle.Internal_Copy(style, source);
        return style;
    }

    [HideFromIl2Cpp]
    private void ReportRenderError(Exception exception)
    {
        var signature = exception.GetType().FullName + ": " + exception.Message;
        if (string.Equals(signature, _lastRenderErrorSignature, StringComparison.Ordinal))
            return;

        _lastRenderErrorSignature = signature;
        Plugin.LogSource.LogError($"Settings menu rendering failed (further identical errors suppressed): {exception}");
    }
}
