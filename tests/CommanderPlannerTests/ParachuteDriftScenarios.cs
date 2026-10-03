using System.Numerics;
using System.Runtime.CompilerServices;
using ER2RealismOverhaul;

internal static class ParachuteDriftScenarios
{
    [ModuleInitializer]
    internal static void RunAll()
    {
        var landingOffsets = new List<Vector2>();
        var descentScales = new List<float>();
        for (int id = 1; id <= 64; id++)
        {
            var profile = ParachuteDriftCore.CreateProfile(id);
            Require(profile == ParachuteDriftCore.CreateProfile(id), "Profiles must remain stable.");
            Vector2 offset = Vector2.Zero;
            Vector2 previous = ParachuteDriftCore.HorizontalVelocity(profile, new Vector2(3f, 0f), offset, 0f, 0f);
            for (int tick = 0; tick < 1500; tick++)
            {
                float time = tick * 0.02f;
                var velocity = ParachuteDriftCore.HorizontalVelocity(profile, new Vector2(3f, 0f), offset, time, time);
                Require(velocity.X > 0f, "Canopies must follow the prevailing wind.");
                Require(Vector2.Distance(velocity, previous) < 0.04f, "Drift must change smoothly.");
                float descent = ParachuteDriftCore.DescentScale(profile, time);
                Require(descent >= 0.61249f && descent <= 0.78751f,
                    "Every canopy must descend more slowly while preserving modest variation.");
                previous = velocity;
                offset += velocity * 0.02f;
                var extreme = ParachuteDriftCore.HorizontalVelocity(profile, new Vector2(1000f, -1000f), offset, time, time);
                Require(float.IsFinite(extreme.X) && extreme.Length() <= 5.00001f, "Custom weather must respect the drift cap.");
            }
            landingOffsets.Add(offset);
            descentScales.Add(profile.DescentScale);
            var calm = ParachuteDriftCore.HorizontalVelocity(profile, Vector2.Zero, Vector2.Zero, 0f, 0f);
            Require(calm.Length() <= 0.55f, "Calm-weather sway must stay gentle.");
            var invalid = ParachuteDriftCore.HorizontalVelocity(profile, new Vector2(float.NaN, float.PositiveInfinity), Vector2.Zero, 0f, 0f);
            Require(invalid == calm, "Invalid weather must fall back to calm motion.");
        }
        Require(landingOffsets.Max(p => p.X) - landingOffsets.Min(p => p.X) > 20f,
            "A squad must develop different downwind landing positions.");
        Require(landingOffsets.Max(p => p.Y) - landingOffsets.Min(p => p.Y) > 10f,
            "A squad must spread across the wind as well.");
        Require(descentScales.Max() - descentScales.Min() > 0.15f,
            "A squad must develop different descent speeds.");

        var sample = ParachuteDriftCore.CreateProfile(42);
        Vector2 Integrate(float dt)
        {
            Vector2 position = Vector2.Zero;
            for (int i = 0; i < (int)MathF.Round(30f / dt); i++)
                position += ParachuteDriftCore.HorizontalVelocity(sample, new Vector2(3f, 0f), position, i * dt, i * dt) * dt;
            return position;
        }
        Require(Vector2.Distance(Integrate(0.02f), Integrate(0.01f)) < 0.03f,
            "Changing physics tick rate must preserve the drop path.");
        Console.WriteLine("PASS ParachuteDrift: squad dispersion, smooth motion, descent bounds, calm/invalid/extreme weather, tick-rate consistency");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
