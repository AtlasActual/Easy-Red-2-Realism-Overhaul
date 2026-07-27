using System.Text;
using ExitGames.Client.Photon;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class SettingsSyncController : MonoBehaviour
{
    private const string ProtocolMarker = "ER2RO_SETTINGS_1";
    private const string RoomPropertyKey = "er2ro.settings";
    private const float RoomStabilityDelaySeconds = 1f;

    private static SettingsSyncController? _instance;

    private readonly Dictionary<string, string> _localBackup = new(StringComparer.Ordinal);
    private string? _publishedPayload;
    private string? _appliedPayload;
    private string? _roomName;
    private BattleLoader? _deferredBattleLoader;
    private float _roomObservedAt;
    private float _nextCheckAt;
    private float _nextFailureLogAt;
    private bool _clientOverrideActive;
    private bool _isRemoteClient;

    internal static bool IsClientOverrideActive => _instance?._clientOverrideActive == true;
    internal static bool AreSettingsReadOnly => _instance?._isRemoteClient == true;
    internal static bool ShouldPersistAppliedSettings => _instance == null || _instance._localBackup.Count == 0;
    internal static string StatusLabel { get; private set; } = "Offline - local settings";

    [HideFromIl2Cpp]
    internal static void NotifySettingsChanged()
    {
        if (_instance == null)
            return;

        _instance._publishedPayload = null;
        _instance._nextCheckAt = 0f;
    }

    private void Awake()
    {
        _instance = this;
    }

    private void Update()
    {
        ContinueDeferredBattleRoomConnect();

        if (Time.unscaledTime < _nextCheckAt)
            return;

        _nextCheckAt = Time.unscaledTime + 0.5f;
        try
        {
            Synchronize();
        }
        catch (Exception ex)
        {
            StatusLabel = "Settings sync temporarily unavailable";
            LogFailure($"Settings synchronization failed and will retry: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        _deferredBattleLoader = null;
        RestoreLocalSettings();
        if (_instance == this)
            _instance = null;
    }

    private void OnApplicationQuit()
    {
        RuntimeLifecycle.IsQuitting = true;
    }

    [HideFromIl2Cpp]
    private void Synchronize()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            LeaveSession();
            return;
        }

        var room = PhotonNetwork.CurrentRoom;
        var currentRoomName = (string?)room.Name ?? string.Empty;

        // ServerListMenu checks whether a room exists by joining it through
        // MultiplayerAuthenticator and immediately leaving again. BattleLoader is
        // absent during that probe, so do not publish or consume session state yet.
        if (BattleLoader.instance == null)
        {
            StatusLabel = "Multiplayer - waiting for battle session";
            return;
        }

        if (!string.Equals(_roomName, currentRoomName, StringComparison.Ordinal))
        {
            _roomName = currentRoomName;
            _roomObservedAt = Time.unscaledTime;
            _publishedPayload = null;
            _appliedPayload = null;
            _clientOverrideActive = false;
            _isRemoteClient = !PhotonNetwork.IsMasterClient;
            StatusLabel = PhotonNetwork.IsMasterClient
                ? "Host - waiting for stable session"
                : "Client - waiting for stable session";
            return;
        }

        // MultiplayerAuthenticator temporarily joins a candidate room, invokes its
        // room-check callback, and immediately calls LeaveRoom. Treating that probe
        // as the gameplay session made a remote client deserialize and apply the
        // entire host snapshot while the game was trying to leave and complete its
        // real join. Only synchronize once the room has remained active.
        if (Time.unscaledTime - _roomObservedAt < RoomStabilityDelaySeconds)
            return;

        if (PhotonNetwork.IsMasterClient)
        {
            _clientOverrideActive = false;
            _isRemoteClient = false;
            StatusLabel = _localBackup.Count == 0
                ? "Host - your settings control this session"
                : "Host - inherited session settings (host migration)";
            PublishHostSettings(room);
        }
        else
        {
            _isRemoteClient = true;
            ReadHostSettings(room);
        }
    }

    [HideFromIl2Cpp]
    internal static bool IsBattleRoomHandoffState(ClientState state)
    {
        return state is ClientState.Leaving
            or ClientState.DisconnectingFromGameServer
            or ClientState.ConnectingToMasterServer;
    }

    [HideFromIl2Cpp]
    internal static bool DeferBattleRoomConnect(BattleLoader battleLoader)
    {
        if (_instance == null)
            return false;

        _instance._deferredBattleLoader = battleLoader;
        return true;
    }

    [HideFromIl2Cpp]
    private void ContinueDeferredBattleRoomConnect()
    {
        var battleLoader = _deferredBattleLoader;
        if (battleLoader == null || !PhotonNetwork.IsConnectedAndReady)
            return;

        _deferredBattleLoader = null;
        try
        {
            Plugin.LogSource.LogInfo(
                "Photon finished the previous room handoff; continuing the deferred next-battle connection.");
            battleLoader.TryConnect();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Could not continue the deferred next-battle connection: {ex.Message}");
        }
    }

    [HideFromIl2Cpp]
    private void LogFailure(string message)
    {
        if (Time.unscaledTime < _nextFailureLogAt)
            return;

        _nextFailureLogAt = Time.unscaledTime + 30f;
        Plugin.LogSource.LogWarning(message);
    }

    [HideFromIl2Cpp]
    private void PublishHostSettings(Photon.Realtime.Room room)
    {
        var payload = BuildPayload();
        if (payload == _publishedPayload)
            return;

        var properties = new Hashtable();
        properties[MakeIl2CppString(RoomPropertyKey)] = MakeIl2CppString(payload);

        if (room.SetCustomProperties(properties))
        {
            _publishedPayload = payload;
            Plugin.LogSource.LogDebug($"Published {SettingsCatalog.All.Count} host settings for the multiplayer session.");
        }
    }

    [HideFromIl2Cpp]
    private void ReadHostSettings(Photon.Realtime.Room room)
    {
        var raw = room.CustomProperties?[MakeIl2CppString(RoomPropertyKey)];
        // Il2CppSystem.String has no registered IL2CPP class pointer, so casting
        // to it throws "not an Il2Cpp reference type". Read the value through the
        // object's native ToString instead.
        var payload = raw == null ? null : raw.ToString();
        if (string.IsNullOrEmpty(payload))
        {
            StatusLabel = "Client - waiting for host settings";
            return;
        }

        if (payload == _appliedPayload)
        {
            _clientOverrideActive = true;
            StatusLabel = "Client - host settings active";
            return;
        }

        if (!TryReadPayload(payload, out var values, out var error))
        {
            _clientOverrideActive = false;
            StatusLabel = "Client - " + error;
            return;
        }

        CaptureLocalSettings();
        ApplySessionValues(values);
        _appliedPayload = payload;
        _clientOverrideActive = true;
        StatusLabel = "Client - host settings active";
        Plugin.LogSource.LogInfo($"Applied {values.Count} host settings for this multiplayer session. The local config file was not changed.");
    }

    [HideFromIl2Cpp]
    private void LeaveSession()
    {
        RestoreLocalSettings();
        _roomName = null;
        _roomObservedAt = 0f;
        _publishedPayload = null;
        _appliedPayload = null;
        _clientOverrideActive = false;
        _isRemoteClient = false;
        StatusLabel = "Offline - local settings";
    }

    [HideFromIl2Cpp]
    private void CaptureLocalSettings()
    {
        if (_localBackup.Count != 0)
            return;

        foreach (var setting in SettingsCatalog.All)
            _localBackup[setting.Id] = setting.Entry.GetSerializedValue();
    }

    [HideFromIl2Cpp]
    private void RestoreLocalSettings()
    {
        if (_localBackup.Count == 0)
            return;

        var config = Settings.ConfigFile;
        var saveOnSet = config.SaveOnConfigSet;
        try
        {
            config.SaveOnConfigSet = false;
            foreach (var setting in SettingsCatalog.All)
            {
                if (_localBackup.TryGetValue(setting.Id, out var value))
                    setting.Entry.SetSerializedValue(value);
            }
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
            _localBackup.Clear();
        }

        Plugin.LogSource.LogInfo("Restored local settings after leaving the multiplayer session.");
    }

    [HideFromIl2Cpp]
    private static void ApplySessionValues(IReadOnlyList<string> values)
    {
        var config = Settings.ConfigFile;
        var saveOnSet = config.SaveOnConfigSet;
        try
        {
            config.SaveOnConfigSet = false;
            for (var index = 0; index < SettingsCatalog.All.Count; index++)
                SettingsCatalog.All[index].Entry.SetSerializedValue(values[index]);
        }
        finally
        {
            config.SaveOnConfigSet = saveOnSet;
        }
    }

    [HideFromIl2Cpp]
    private static Il2CppSystem.Object MakeIl2CppString(string value)
        => new(IL2CPP.ManagedStringToIl2Cpp(value));

    [HideFromIl2Cpp]
    private static string BuildPayload()
    {
        var builder = new StringBuilder(2048);
        builder.Append(ProtocolMarker).Append('\n');
        builder.Append(Plugin.PluginVersion).Append('\n');
        builder.Append(SettingsCatalog.All.Count);
        foreach (var setting in SettingsCatalog.All)
            builder.Append('\n').Append(setting.Entry.GetSerializedValue());
        return builder.ToString();
    }

    [HideFromIl2Cpp]
    private static bool TryReadPayload(string payload, out IReadOnlyList<string> values, out string error)
    {
        var lines = payload.Split('\n');
        if (lines.Length < 3 || lines[0] != ProtocolMarker)
        {
            values = Array.Empty<string>();
            error = "host uses an unknown settings protocol";
            return false;
        }

        if (lines[1] != Plugin.PluginVersion)
        {
            values = Array.Empty<string>();
            error = $"version mismatch (host {lines[1]}, local {Plugin.PluginVersion})";
            return false;
        }

        if (!int.TryParse(lines[2], out var count) || count != SettingsCatalog.All.Count || lines.Length != count + 3)
        {
            values = Array.Empty<string>();
            error = "host has a different settings catalog";
            return false;
        }

        values = lines.Skip(3).ToArray();
        error = string.Empty;
        return true;
    }
}

[HarmonyPatch(typeof(BattleLoader), nameof(BattleLoader.TryConnect))]
internal static class BattleLoaderRoomHandoffPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BattleLoader __instance)
    {
        try
        {
            var matchData = MatchData.data;
            var multiplayerIntent = matchData != null && matchData.isMultiplayer;
            var state = PhotonNetwork.NetworkClientState;
            if (!multiplayerIntent || PhotonNetwork.IsConnectedAndReady)
                return true;

            if (!SettingsSyncController.IsBattleRoomHandoffState(state))
                return true;

            if (!SettingsSyncController.DeferBattleRoomConnect(__instance))
                return true;

            Plugin.LogSource.LogInfo(
                $"Deferred the next-battle room connection while Photon finishes the previous room handoff ({state}).");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Battle room handoff guard failed safely; using the game's original connection path: {ex.Message}");
            return true;
        }
    }
}
