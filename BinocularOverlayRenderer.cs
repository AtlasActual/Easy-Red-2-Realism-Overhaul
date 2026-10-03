using UnityEngine;

namespace ER2RealismOverhaul;

internal sealed class BinocularOverlayRenderer
{
    private const int TextureWidth = 1024;
    private const float LensOffset = 0.46f;
    private const float Border = 0.04f;
    private const float RadiusHeightFraction = 0.47f;
    private const float SettleDuration = 0.24f;

    private Texture2D? _texture;
    private float _raisedAt;

    internal void Raise()
    {
        EnsureTexture();
        _raisedAt = Time.unscaledTime;
    }

    internal void Draw()
    {
        var width = (float)Screen.width;
        var height = (float)Screen.height;
        if (width <= 0f || height <= 0f)
            return;

        EnsureTexture();
        var settle = Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((Time.unscaledTime - _raisedAt) / SettleDuration));
        // A small eye-relief adjustment as the eyepieces settle against the face.
        // Scale both axes equally to retain circular pupils on every aspect ratio.
        var radius = Mathf.Min(height * RadiusHeightFraction,
            width * 0.48f / (1f + LensOffset + Border)) * Mathf.Lerp(0.94f, 1f, settle);
        var halfWidth = radius * (1f + LensOffset + Border);
        var halfHeight = radius * (1f + Border);
        var centerY = height * (0.5f + 0.012f * (1f - settle));
        var rect = new Rect(width * 0.5f - halfWidth, centerY - halfHeight,
            halfWidth * 2f, halfHeight * 2f);

        var previousColor = GUI.color;
        try
        {
            GUI.color = Color.black;
            // Overlap the texture's fully opaque border by one pixel so fractional
            // IMGUI bounds cannot expose pinholes around the outside of the mask.
            GUI.DrawTexture(new Rect(0f, 0f, width, rect.yMin + 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, rect.yMax - 1f, width, height - rect.yMax + 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, rect.yMin, rect.xMin + 1f, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - 1f, rect.yMin, width - rect.xMax + 1f, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, _texture);
        }
        finally
        {
            GUI.color = previousColor;
        }
    }

    internal void Destroy()
    {
        if (_texture != null)
            UnityEngine.Object.Destroy(_texture);
        _texture = null;
    }

    private void EnsureTexture()
    {
        if (_texture != null)
            return;

        var halfWidth = 1f + LensOffset + Border;
        var halfHeight = 1f + Border;
        var textureHeight = (int)MathF.Round(TextureWidth * halfHeight / halfWidth);
        var pixels = new Color[TextureWidth * textureHeight];
        for (var y = 0; y < textureHeight; y++)
        {
            var py = ((y + 0.5f) / textureHeight * 2f - 1f) * halfHeight;
            for (var x = 0; x < TextureWidth; x++)
            {
                var px = ((x + 0.5f) / TextureWidth * 2f - 1f) * halfWidth;
                // Union of the two pupils: the overlap stays clear, without an
                // artificial dark seam or a double image in the viewing center.
                var dx = MathF.Abs(px) - LensOffset;
                var distance = MathF.Sqrt(dx * dx + py * py);
                var vignette = 0.42f * SmoothRange(0.64f, 1f, distance);
                var cutoff = SmoothRange(0.965f, 1.006f, distance);
                var darkness = vignette + (1f - vignette) * cutoff;

                // Faint warm/cool glancing reflections confined to the lens rim.
                // The central image is completely transparent and untinted.
                var rim = SmoothRange(0.80f, 0.93f, distance) *
                          (1f - SmoothRange(0.93f, 1f, distance));
                var reflection = rim * 0.035f * MathF.Abs(py) * (1f - cutoff);
                var alpha = darkness + reflection * (1f - darkness);
                var reflectionWeight = alpha > 0f ? reflection * (1f - darkness) / alpha : 0f;
                pixels[y * TextureWidth + x] = new Color
                {
                    r = (py > 0f ? 0.52f : 0.22f) * reflectionWeight,
                    g = (py > 0f ? 0.38f : 0.34f) * reflectionWeight,
                    b = (py > 0f ? 0.20f : 0.48f) * reflectionWeight,
                    a = alpha
                };
            }
        }

        var texture = new Texture2D(TextureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            name = "ER2 Realism Binocular Optics",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _texture = texture;
        }
        catch
        {
            UnityEngine.Object.Destroy(texture);
            throw;
        }
    }

    private static float SmoothRange(float from, float to, float value)
    {
        // Keep the pixel loop entirely managed; millions of IL2CPP math calls
        // would otherwise hitch the first time the player raises binoculars.
        var t = Math.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
