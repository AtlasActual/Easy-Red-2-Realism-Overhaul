using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[Flags]
internal enum AiDebugCategory
{
    None = 0,
    Identity = 1 << 0,
    Perception = 1 << 1,
    Navigation = 1 << 2,
    Combat = 1 << 3,
    Danger = 1 << 4,
    Command = 1 << 5,
    Vehicle = 1 << 6,
    Support = 1 << 8,
    Events = 1 << 9,
    Performance = 1 << 10,
    All = Identity | Perception | Navigation | Combat | Danger | Command |
          Vehicle | Support | Events | Performance
}

internal enum AiDebugProfileKind
{
    Soldier,
    Vehicle,
    Battle
}

internal readonly record struct AiDebugEvent(
    float At,
    AiDebugCategory Category,
    int EntityId,
    string Message);

internal readonly record struct AiDebugProfileSnapshot(
    AiDebugProfileKind Kind,
    int Calls,
    float TotalMilliseconds,
    float MaximumMilliseconds)
{
    internal float AverageMilliseconds => Calls > 0 ? TotalMilliseconds / Calls : 0f;
}

/// <summary>
/// Bounded, local-only diagnostics bridge. It is a no-op while the overlay is
/// hidden, so normal play does not allocate event strings or time AI updates.
/// </summary>
internal static class AiDebugTelemetry
{
    private const int MaximumEvents = 384;
    private static readonly List<AiDebugEvent> Events = new(MaximumEvents);
    private static readonly Dictionary<AiDebugProfileKind, ProfileAccumulator> Profiles = new();
    private static readonly Dictionary<(AiDebugCategory Category, int EntityId, string Message), float>
        LastDecisionAt = new();

    internal static bool CaptureEnabled { get; set; }

    internal static void Clear()
    {
        Events.Clear();
        Profiles.Clear();
        LastDecisionAt.Clear();
    }

    internal static void RecordTrace(string message)
    {
        if (!CaptureEnabled || string.IsNullOrWhiteSpace(message))
            return;

        Record(InferCategory(message), ExtractEntityId(message), message);
    }

    internal static void RecordDecision(
        AiDebugCategory category,
        int entityId,
        string message)
    {
        if (!CaptureEnabled || string.IsNullOrWhiteSpace(message))
            return;
        var now = Time.unscaledTime;
        var key = (category, entityId, message);
        if (LastDecisionAt.TryGetValue(key, out var lastAt) && now - lastAt < 0.35f)
            return;
        LastDecisionAt[key] = now;
        Record(category, entityId, message);
    }

    internal static void CopyEvents(
        float now,
        float historySeconds,
        List<AiDebugEvent> destination)
    {
        destination.Clear();
        var oldest = now - Mathf.Max(1f, historySeconds);
        var removeCount = 0;
        while (removeCount < Events.Count && Events[removeCount].At < oldest)
            removeCount++;
        if (removeCount > 0)
            Events.RemoveRange(0, removeCount);
        destination.AddRange(Events);
    }

    internal static long BeginProfile()
        => CaptureEnabled ? Stopwatch.GetTimestamp() : 0L;

    internal static void EndProfile(AiDebugProfileKind kind, long startedAt)
    {
        if (startedAt == 0L || !CaptureEnabled)
            return;

        var elapsedMilliseconds = (Stopwatch.GetTimestamp() - startedAt) *
                                  (1000.0 / Stopwatch.Frequency);
        if (!Profiles.TryGetValue(kind, out var profile))
        {
            profile = new ProfileAccumulator { WindowStartedAt = Time.unscaledTime };
            Profiles[kind] = profile;
        }

        profile.Calls++;
        profile.TotalMilliseconds += elapsedMilliseconds;
        profile.MaximumMilliseconds = Math.Max(profile.MaximumMilliseconds, elapsedMilliseconds);
    }

    internal static void CopyProfiles(float now, List<AiDebugProfileSnapshot> destination)
    {
        destination.Clear();
        foreach (var kind in Enum.GetValues<AiDebugProfileKind>())
        {
            if (!Profiles.TryGetValue(kind, out var profile))
                continue;

            if (now - profile.WindowStartedAt >= 1f)
            {
                profile.LastCalls = profile.Calls;
                profile.LastTotalMilliseconds = profile.TotalMilliseconds;
                profile.LastMaximumMilliseconds = profile.MaximumMilliseconds;
                profile.Calls = 0;
                profile.TotalMilliseconds = 0;
                profile.MaximumMilliseconds = 0;
                profile.WindowStartedAt = now;
            }

            var calls = profile.LastCalls != 0 ? profile.LastCalls : profile.Calls;
            var total = profile.LastCalls != 0
                ? profile.LastTotalMilliseconds
                : profile.TotalMilliseconds;
            var maximum = profile.LastCalls != 0
                ? profile.LastMaximumMilliseconds
                : profile.MaximumMilliseconds;
            destination.Add(new AiDebugProfileSnapshot(
                kind, calls, (float)total, (float)maximum));
        }
    }

    private static void Record(AiDebugCategory category, int entityId, string message)
    {
        if (Events.Count >= MaximumEvents)
            Events.RemoveRange(0, Math.Min(32, Events.Count));
        Events.Add(new AiDebugEvent(Time.unscaledTime, category, entityId, message));
    }

    private static AiDebugCategory InferCategory(string message)
    {
        var text = message.ToLowerInvariant();
        if (text.Contains("artillery") || text.Contains("smoke") || text.Contains("support"))
            return AiDebugCategory.Support;
        if (text.Contains("vehicle") || text.Contains("tank tactic") || text.Contains("transport"))
            return AiDebugCategory.Vehicle;
        if (text.Contains("commander") || text.Contains("director") || text.Contains("order") ||
            text.Contains("contact direct") || text.Contains("contact voice") || text.Contains("contact radio"))
            return AiDebugCategory.Command;
        if (text.Contains("cover") || text.Contains("relocat") || text.Contains("movement") ||
            text.Contains("destination") || text.Contains("stall"))
            return AiDebugCategory.Navigation;
        if (text.Contains("suppress") || text.Contains("danger") || text.Contains("fear") ||
            text.Contains("incoming") || text.Contains("flame") || text.Contains("grenade"))
            return AiDebugCategory.Danger;
        if (text.Contains("target") || text.Contains("perception") || text.Contains("observ") ||
            text.Contains("spot") || text.Contains("gunfire"))
            return AiDebugCategory.Perception;
        if (text.Contains("fire") || text.Contains("weapon") || text.Contains("aim") ||
            text.Contains("emplacement"))
            return AiDebugCategory.Combat;
        return AiDebugCategory.Events;
    }

    private static int ExtractEntityId(string message)
    {
        var text = message.AsSpan();
        foreach (var marker in new[] { "soldier ", "vehicle " })
        {
            var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;
            var digits = text[(markerIndex + marker.Length)..];
            var length = 0;
            while (length < digits.Length && char.IsDigit(digits[length]))
                length++;
            if (length > 0 && int.TryParse(digits[..length], out var id))
                return id;
        }
        return 0;
    }

    private sealed class ProfileAccumulator
    {
        internal float WindowStartedAt;
        internal int Calls;
        internal double TotalMilliseconds;
        internal double MaximumMilliseconds;
        internal int LastCalls;
        internal double LastTotalMilliseconds;
        internal double LastMaximumMilliseconds;
    }
}

[HarmonyPatch(typeof(SoldierAI), "SequentialUpdate")]
internal static class AiDebugSoldierProfilerPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(out long __state) => __state = AiDebugTelemetry.BeginProfile();

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(long __state)
        => AiDebugTelemetry.EndProfile(AiDebugProfileKind.Soldier, __state);
}

[HarmonyPatch(typeof(AIVehicle), "Update")]
internal static class AiDebugVehicleProfilerPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(out long __state) => __state = AiDebugTelemetry.BeginProfile();

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(long __state)
        => AiDebugTelemetry.EndProfile(AiDebugProfileKind.Vehicle, __state);
}

[HarmonyPatch(typeof(BattleManager), "Update")]
internal static class AiDebugBattleProfilerPatch
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(out long __state) => __state = AiDebugTelemetry.BeginProfile();

    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(long __state)
        => AiDebugTelemetry.EndProfile(AiDebugProfileKind.Battle, __state);
}
