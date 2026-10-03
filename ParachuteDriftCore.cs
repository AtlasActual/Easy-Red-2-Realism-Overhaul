using System.Numerics;

namespace ER2RealismOverhaul;

internal readonly record struct ParachuteProfile(
    float DescentScale, float WindScale, float WindAngle, float SwaySpeed,
    float Phase, float Frequency);

internal static class ParachuteDriftCore
{
    // Give an open canopy a longer descent while retaining individual variation.
    private const float OverallDescentScale = 0.7f;

    // Sample once per canopy without consuming Unity's gameplay random stream.
    internal static ParachuteProfile CreateProfile(int id)
    {
        uint seed = unchecked((uint)id);
        float Sample()
        {
            seed += 0x9e3779b9u;
            uint value = seed;
            value = (value ^ (value >> 16)) * 0x85ebca6bu;
            value = (value ^ (value >> 13)) * 0xc2b2ae35u;
            value ^= value >> 16;
            return (value & 0xffffffu) / 16777215f;
        }
        return new ParachuteProfile(
            0.9f + Sample() * 0.2f,
            0.8f + Sample() * 0.4f,
            (Sample() - 0.5f) * (MathF.PI / 9f),
            0.15f + Sample() * 0.3f,
            Sample() * MathF.Tau,
            0.65f + Sample() * 0.4f);
    }

    internal static float DescentScale(ParachuteProfile profile, float age)
        => OverallDescentScale *
            (profile.DescentScale + 0.025f * MathF.Sin(age * profile.Frequency + profile.Phase));

    internal static Vector2 HorizontalVelocity(
        ParachuteProfile profile, Vector2 mapWind, Vector2 position, float time, float age)
    {
        if (!float.IsFinite(mapWind.X) || !float.IsFinite(mapWind.Y))
            mapWind = Vector2.Zero;
        float speed = mapWind.Length();
        if (speed > 5f)
            mapWind *= 5f / speed;

        // A broad gust field gives nearby canopies related weather; each canopy
        // responds a little differently. Sway remains present on calm maps.
        float gust = 1f + 0.12f * MathF.Sin(time * 0.35f + position.X * 0.012f + position.Y * 0.009f);
        float angle = profile.WindAngle;
        var wind = new Vector2(
            mapWind.X * MathF.Cos(angle) - mapWind.Y * MathF.Sin(angle),
            mapWind.X * MathF.Sin(angle) + mapWind.Y * MathF.Cos(angle));
        float phase = age * profile.Frequency + profile.Phase;
        var sway = new Vector2(MathF.Sin(phase), 0.7f * MathF.Cos(phase * 0.83f + profile.Phase))
            * profile.SwaySpeed;
        var velocity = wind * (profile.WindScale * gust) + sway;
        float length = velocity.Length();
        return length > 5f ? velocity * (5f / length) : velocity;
    }
}
