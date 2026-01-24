using UnityEngine;

namespace Tsvrc.UI.Utils
{
    public static class TextureGraphics2D
    {
        public static void FillTexture(Texture2D texture, Color fillColor)
        {
            Color32[] pixels = new Color32[texture.width * texture.height];
            Color32 fillColor32 = fillColor;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fillColor32;
            texture.SetPixels32(pixels);
        }

        public static void ClearTexture(Texture2D texture)
        {
            Color32[] clearPixels = new Color32[texture.width * texture.height];
            texture.SetPixels32(clearPixels);
        }

        public static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, int thickness, Color color)
        {
            // Bresenham's line algorithm with thickness
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                // Draw thick point
                for (int tx = -thickness / 2; tx <= thickness / 2; tx++)
                {
                    for (int ty = -thickness / 2; ty <= thickness / 2; ty++)
                    {
                        int px = x0 + tx;
                        int py = y0 + ty;
                        if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                            texture.SetPixel(px, py, color);
                    }
                }

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        public static void DrawHorizontalLine(Texture2D texture, int x, int y, int length, int thickness, Color color)
        {
            for (int i = 0; i < length; i++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    if (x + i < texture.width && y + t < texture.height)
                        texture.SetPixel(x + i, y + t, color);
                }
            }
        }

        public static void DrawVerticalLine(Texture2D texture, int x, int y, int length, int thickness, Color color)
        {
            for (int i = 0; i < length; i++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    if (x + t < texture.width && y + i < texture.height)
                        texture.SetPixel(x + t, y + i, color);
                }
            }
        }

        public static void DrawCircle(Texture2D texture, int x, int y, int radius, Color color)
        {
            for (int i = -radius; i <= radius; i++)
            {
                for (int j = -radius; j <= radius; j++)
                {
                    // Circle equation: x^2 + y^2 <= r^2
                    if (i * i + j * j <= radius * radius)
                    {
                        int px = x + i;
                        int py = y + j;
                        if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                            texture.SetPixel(px, py, color);
                    }
                }
            }
        }
    }
}