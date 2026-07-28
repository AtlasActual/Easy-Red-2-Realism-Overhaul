using HarmonyLib;
using UnityEngine;

namespace ER2RealismOverhaul;

[HarmonyPatch(typeof(TracerBehaviour), nameof(TracerBehaviour.OnEnable))]
internal static class TracerVisualPatch
{
    private static readonly Dictionary<int, TracerVisualBaseline> Baselines = new();
    private static bool _warned;

    [HarmonyPostfix]
    private static void Postfix(TracerBehaviour __instance)
    {
        try
        {
            var id = __instance.GetInstanceID();
            if (!Baselines.TryGetValue(id, out var baseline) ||
                baseline.TracerPointer != __instance.Pointer)
            {
                baseline = TracerVisualBaseline.Capture(__instance);
                Baselines[id] = baseline;
            }

            baseline.Apply(
                Settings.TracerBrightness.Value,
                Settings.TracerSizeMultiplier.Value,
                Settings.TracerLengthMultiplier.Value);
        }
        catch (Exception ex)
        {
            if (_warned)
                return;

            _warned = true;
            Plugin.LogSource.LogWarning($"Tracer visual adjustment unavailable: {ex.Message}");
        }
    }

    private sealed class TracerVisualBaseline
    {
        private readonly LightBaseline[] _lights;
        private readonly RendererMaterialBaseline[] _materials;
        private readonly TransformBaseline[] _transforms;

        private TracerVisualBaseline(
            IntPtr tracerPointer,
            LightBaseline[] lights,
            RendererMaterialBaseline[] materials,
            TransformBaseline[] transforms)
        {
            TracerPointer = tracerPointer;
            _lights = lights;
            _materials = materials;
            _transforms = transforms;
        }

        internal IntPtr TracerPointer { get; }

        internal static TracerVisualBaseline Capture(TracerBehaviour tracer)
        {
            var lights = tracer.GetComponentsInChildren<Light>(true)
                .Select(light => new LightBaseline(light, light.intensity))
                .ToArray();
            var materials = new List<RendererMaterialBaseline>();
            var transforms = new List<TransformBaseline>();
            var capturedTransforms = new HashSet<int>();

            foreach (var renderer in tracer.GetComponentsInChildren<Renderer>(true))
            {
                var visualTransform = renderer.transform;
                if (capturedTransforms.Add(visualTransform.GetInstanceID()))
                    transforms.Add(new TransformBaseline(visualTransform, visualTransform.localScale));

                var sharedMaterials = renderer.sharedMaterials;
                for (var index = 0; index < sharedMaterials.Length; index++)
                {
                    var material = sharedMaterials[index];
                    if (material == null)
                        continue;

                    var baseline = RendererMaterialBaseline.Capture(renderer, material, index);
                    if (baseline != null)
                        materials.Add(baseline);
                }
            }

            return new TracerVisualBaseline(
                tracer.Pointer,
                lights,
                materials.ToArray(),
                transforms.ToArray());
        }

        internal void Apply(float brightness, float size, float length)
        {
            foreach (var transform in _transforms)
                transform.Apply(size, length);
            foreach (var light in _lights)
                light.Apply(brightness);
            foreach (var material in _materials)
                material.Apply(brightness);
        }
    }

    private sealed class LightBaseline
    {
        private readonly Light _light;
        private readonly float _intensity;

        internal LightBaseline(Light light, float intensity)
        {
            _light = light;
            _intensity = intensity;
        }

        internal void Apply(float brightness) => _light.intensity = _intensity * brightness;
    }

    private sealed class TransformBaseline
    {
        private readonly Transform _transform;
        private readonly Vector3 _localScale;

        internal TransformBaseline(Transform transform, Vector3 localScale)
        {
            _transform = transform;
            _localScale = localScale;
        }

        internal void Apply(float size, float length) =>
            _transform.localScale = new Vector3(
                _localScale.x * size,
                _localScale.y * size,
                _localScale.z * length);
    }

    private sealed class RendererMaterialBaseline
    {
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        private readonly Renderer _renderer;
        private readonly int _materialIndex;
        private readonly MaterialPropertyBlock _properties;
        private readonly bool _hasEmission;
        private readonly Color _emission;
        private readonly int _baseColorProperty;
        private readonly Color _baseColor;

        private RendererMaterialBaseline(
            Renderer renderer,
            int materialIndex,
            MaterialPropertyBlock properties,
            bool hasEmission,
            Color emission,
            int baseColorProperty,
            Color baseColor)
        {
            _renderer = renderer;
            _materialIndex = materialIndex;
            _properties = properties;
            _hasEmission = hasEmission;
            _emission = emission;
            _baseColorProperty = baseColorProperty;
            _baseColor = baseColor;
        }

        internal static RendererMaterialBaseline? Capture(
            Renderer renderer,
            Material material,
            int materialIndex)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties, materialIndex);

            var hasEmission = material.HasProperty(EmissionColor);
            var emission = hasEmission
                ? ReadColor(properties, material, EmissionColor)
                : default;
            var baseColorProperty = material.HasProperty(BaseColor)
                ? BaseColor
                : material.HasProperty(ColorProperty)
                    ? ColorProperty
                    : -1;

            if (!hasEmission && baseColorProperty < 0)
                return null;

            var baseColor = baseColorProperty >= 0
                ? ReadColor(properties, material, baseColorProperty)
                : default;
            return new RendererMaterialBaseline(
                renderer,
                materialIndex,
                properties,
                hasEmission,
                emission,
                baseColorProperty,
                baseColor);
        }

        internal void Apply(float brightness)
        {
            _renderer.GetPropertyBlock(_properties, _materialIndex);
            if (_hasEmission)
                _properties.SetColor(EmissionColor, ScaleRgb(_emission, brightness));
            if (_baseColorProperty >= 0)
                _properties.SetColor(_baseColorProperty, ScaleRgb(_baseColor, brightness));
            _renderer.SetPropertyBlock(_properties, _materialIndex);
        }

        private static Color ReadColor(
            MaterialPropertyBlock properties,
            Material material,
            int property)
        {
            return properties.HasColor(property)
                ? properties.GetColor(property)
                : material.GetColor(property);
        }

        private static Color ScaleRgb(Color color, float multiplier) =>
            new(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
    }
}
