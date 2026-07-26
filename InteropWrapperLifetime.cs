using Il2CppInterop.Runtime.InteropTypes;

namespace ER2RealismOverhaul;

/// <summary>
/// Keeps the managed wrappers for the two hottest long-lived IL2CPP objects alive for
/// exactly their native lifetime. Il2CppInterop normally keeps only a weak reference to
/// a wrapper. A managed collection can therefore finalize every active Soldier/SoldierAI
/// wrapper, free its IL2CPP GC handle, and remove it from the shared object-pool map; the
/// next native-to-managed call immediately recreates both the wrapper and handle.
///
/// These strong references turn that collection-time churn into one wrapper per live
/// object. Existing OnDestroy hooks release each entry, and the battle-start reset is the
/// backstop for scene teardown, so this is a bounded lifetime cache rather than a leak.
/// </summary>
internal static class InteropWrapperLifetime
{
    private static readonly Dictionary<IntPtr, Il2CppObjectBase> Retained = new();

    internal static int Count => Retained.Count;

    internal static void Retain(Il2CppObjectBase? wrapper)
    {
        if (wrapper == null)
            return;

        try
        {
            Retain(wrapper, wrapper.Pointer);
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
        }
    }

    internal static void Retain(Il2CppObjectBase wrapper, IntPtr pointer)
    {
        if (pointer != IntPtr.Zero &&
            (!Retained.TryGetValue(pointer, out var retained) ||
             !ReferenceEquals(retained, wrapper)))
        {
            Retained[pointer] = wrapper;
        }
    }

    internal static void Release(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
            Retained.Remove(pointer);
    }

    internal static void Release(Il2CppObjectBase? wrapper)
    {
        if (wrapper == null)
            return;

        try
        {
            Release(wrapper.Pointer);
        }
        catch (Il2CppInterop.Runtime.Il2CppException)
        {
        }
        catch (Il2CppInterop.Runtime.ObjectCollectedException)
        {
        }
    }

    internal static void ResetBattle() => Retained.Clear();
}
