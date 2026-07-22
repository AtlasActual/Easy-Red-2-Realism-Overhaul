namespace ER2RealismOverhaul;

internal enum ContactDeliveryKind
{
    Direct,
    Voice,
    Radio
}

/// <summary>
/// Governs whether an incoming contact report replaces one already stored for the
/// same target, and how a stored report's confidence decays with age.
/// </summary>
internal static class ContactReportStoreCore
{
    // Newer information always wins; delivery directness only breaks exact ties.
    internal static bool ShouldReplace(
        float existingObservedAt, ContactDeliveryKind existingKind,
        float incomingObservedAt, ContactDeliveryKind incomingKind)
        => incomingObservedAt > existingObservedAt ||
           (incomingObservedAt == existingObservedAt && incomingKind < existingKind);

    internal static float DecayedConfidence(float initialConfidence, float ageSeconds, float lifetimeSeconds)
        => initialConfidence * Math.Clamp(1f - ageSeconds / Math.Max(0.1f, lifetimeSeconds), 0f, 1f);
}
