using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace ER2RealismOverhaul;

/// <summary>
/// Il2CppInterop wraps a game object as the type the call site DECLARES - a Harmony
/// argument, a generic return such as <c>FindItemOfType&lt;T&gt;()</c> (declared
/// <c>VirtualItem</c>), a list element - never as its runtime class. A C# <c>is</c> or
/// <c>as</c> against a subclass therefore only succeeds when some other call happens to
/// have wrapped the same object as that subclass and the wrapper is still alive. For
/// <c>Soldier</c> that is nearly always true; for the grenade item classes it never
/// was, which left every check written that way permanently false. These helpers ask
/// the IL2CPP runtime for the real class and allocate only when the wrapper is wrong.
/// </summary>
internal static class Il2CppTypeTests
{
    internal static T? As<T>(Il2CppObjectBase? value) where T : Il2CppObjectBase
    {
        if (value is T typed)
            return typed;
        if (ReferenceEquals(value, null))
            return null;

        try
        {
            return value.TryCast<T>();
        }
        catch (ObjectCollectedException)
        {
            return null;
        }
    }

    internal static bool Is<T>(Il2CppObjectBase? value) where T : Il2CppObjectBase
        => !ReferenceEquals(As<T>(value), null);
}
