using System;
using EFT;
using EFT.UI;
using UnityEngine;

namespace Transmogged
{
    internal static class Art
    {
        public static readonly Color Violet = new Color(0.72f, 0.45f, 1f, 1f);
        public static readonly Color VioletDeep = new Color(0.36f, 0.14f, 0.72f, 1f);
        public static readonly Color VioletEdge = new Color(0.88f, 0.74f, 1f, 1f);

        private static Sprite _glow;
        private static Sprite _panel;

        public static Sprite Arrows
        {
            get
            {
                try
                {
                    return EFTHardSettings.Instance?.StaticIcons?.BarterSign;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static Sprite Panel => _panel ?? (_panel = Rounded(32, 32, 6,
            new Color(0.16f, 0.1f, 0.24f, 0.92f), new Color(0.08f, 0.06f, 0.12f, 0.92f), new Color(0.55f, 0.35f, 0.85f, 0.9f)));

        public static Sprite Glow => _glow ?? (_glow = SoftGlow(64));

        private static Sprite _hex;
        private static Sprite _corner;
        private static Sprite _hidden;

        public static Sprite Hex => _hex ?? (_hex = Hexagon(15.5f, Violet, VioletDeep, VioletEdge));

        public static Sprite Corner => _corner ?? (_corner = Triangle(22));

        public static Sprite HiddenIcon => _hidden ?? (_hidden = NoEntry(96));

        private static Sprite NoEntry(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            float half = size / 2f, radius = half - 8f, width = 3.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half, py = y + 0.5f - half;
                    float length = Mathf.Sqrt(px * px + py * py);
                    float ring = Math.Abs(length - radius);
                    float slash = length < radius ? Math.Abs(px + py) * 0.7071f : float.MaxValue;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(width + 0.5f - Math.Min(ring, slash)));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite Triangle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(y - x + 0.5f));
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite Hexagon(float apothem, Color top, Color bottom, Color rim)
        {
            int w = Mathf.CeilToInt(apothem * 2f / 0.866025f) + 2;
            int h = Mathf.CeilToInt(apothem * 2f) + 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                Color fill = Color.Lerp(bottom, top, (float)y / (h - 1));
                for (int x = 0; x < w; x++)
                {
                    float d = -HexDistance(x + 0.5f - w / 2f, y + 0.5f - h / 2f, apothem);
                    Color c = d < 1.6f ? Color.Lerp(rim, fill, Mathf.Clamp01(d - 0.6f)) : fill;
                    c.a *= Mathf.Clamp01(d + 0.5f);
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        private static float HexDistance(float px, float py, float apothem)
        {
            const float kx = -0.866025404f, ky = 0.5f, kz = 0.577350269f;
            px = Math.Abs(px);
            py = Math.Abs(py);
            float dot = Math.Min(kx * px + ky * py, 0f);
            px -= 2f * dot * kx;
            py -= 2f * dot * ky;
            px -= Mathf.Clamp(px, -kz * apothem, kz * apothem);
            py -= apothem;
            return Mathf.Sqrt(px * px + py * py) * Math.Sign(py);
        }

        private static Sprite Rounded(int w, int h, int radius, Color top, Color bottom, Color rim)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                Color fill = Color.Lerp(bottom, top, (float)y / (h - 1));
                for (int x = 0; x < w; x++)
                {
                    float d = EdgeDistance(x + 0.5f, y + 0.5f, w, h, radius);
                    float alpha = Mathf.Clamp01(d + 0.5f);
                    Color c = d < 1.5f ? Color.Lerp(rim, fill, Mathf.Clamp01(d - 0.5f)) : fill;
                    c.a *= alpha;
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            float b = radius + 1;
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static float EdgeDistance(float x, float y, int w, int h, int r)
        {
            float dx = Math.Max(Math.Max(r - x, x - (w - r)), 0f);
            float dy = Math.Max(Math.Max(r - y, y - (h - r)), 0f);
            if (dx > 0f && dy > 0f)
                return r - Mathf.Sqrt(dx * dx + dy * dy);
            return Math.Min(Math.Min(x, w - x), Math.Min(y, h - y));
        }

        private static Sprite SoftGlow(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                    float a = Mathf.Clamp01(1f - d);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
