using UnityEngine;

namespace ER2RealismOverhaul;

internal static class AudioVoiceCapacity
{
    internal static void ApplyAtStartup()
    {
        try
        {
            var original = AudioSettings.GetConfiguration();
            if (!Settings.RaiseAudioVoiceCapacity.Value)
            {
                Plugin.LogSource.LogInfo(
                    $"Audio voice capacity unchanged: real={original.numRealVoices}, " +
                    $"virtual={original.numVirtualVoices} (startup increase disabled).");
                return;
            }

            var requestedReal = Math.Max(original.numRealVoices, Settings.MinimumRealAudioVoices.Value);
            var requestedVirtual = Math.Max(
                original.numVirtualVoices,
                Math.Max(requestedReal, Settings.MinimumVirtualAudioVoices.Value));

            if (requestedReal == original.numRealVoices &&
                requestedVirtual == original.numVirtualVoices)
            {
                Plugin.LogSource.LogInfo(
                    $"Audio voice capacity already meets the configured minimums: " +
                    $"real={original.numRealVoices}, virtual={original.numVirtualVoices}.");
                return;
            }

            var requested = original;
            requested.numRealVoices = requestedReal;
            requested.numVirtualVoices = requestedVirtual;

            var resetSucceeded = AudioSettings.Reset(requested);
            var accepted = AudioSettings.GetConfiguration();
            if (!resetSucceeded)
            {
                Plugin.LogSource.LogWarning(
                    $"Unity rejected the requested audio voice capacity. " +
                    $"Original real/virtual={original.numRealVoices}/{original.numVirtualVoices}, " +
                    $"requested={requestedReal}/{requestedVirtual}, " +
                    $"current={accepted.numRealVoices}/{accepted.numVirtualVoices}.");
                return;
            }

            if (accepted.numRealVoices < requestedReal ||
                accepted.numVirtualVoices < requestedVirtual)
            {
                Plugin.LogSource.LogWarning(
                    $"Unity accepted only part of the requested audio voice capacity. " +
                    $"Original real/virtual={original.numRealVoices}/{original.numVirtualVoices}, " +
                    $"requested={requestedReal}/{requestedVirtual}, " +
                    $"accepted={accepted.numRealVoices}/{accepted.numVirtualVoices}.");
                return;
            }

            Plugin.LogSource.LogInfo(
                $"Audio voice capacity raised: " +
                $"real {original.numRealVoices}->{accepted.numRealVoices}, " +
                $"virtual {original.numVirtualVoices}->{accepted.numVirtualVoices}.");
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogWarning(
                $"Could not change Unity's audio voice capacity; keeping its current configuration: {ex.Message}");
        }
    }
}
