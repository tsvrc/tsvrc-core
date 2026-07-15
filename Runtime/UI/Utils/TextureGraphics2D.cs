using UnityEngine;

namespace Tsvrc.UI.Utils
{
    /// <summary>
    /// Static utility for pixel-level drawing on Unity textures.
    /// Buffer-based methods (names ending in ToBuffer) write to CPU memory only and need
    /// a <see cref="FlushBuffer"/> call to upload the result to the GPU.
    /// The texture-based overloads call SetPixel directly and are slower but do not require
    /// a pre-allocated buffer.
    /// </summary>
    public static class TextureGraphics2D
    {
        /// <summary>
        /// Fills a texture with a solid color. Allocates a temporary pixel array and calls
        /// SetPixels32, but does not call Apply. If you already have a buffer, use
        /// <see cref="FillBuffer"/> followed by <see cref="FlushBuffer"/> instead.
        /// </summary>
        public static void FillTexture(Texture2D texture, Color fillColor)
        {
            Color32[] pixels = new Color32[texture.width * texture.height];
            Color32 fillColor32 = fillColor;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = fillColor32;
            texture.SetPixels32(pixels);
        }

        /// <summary>
        /// Clears a texture to fully transparent (0,0,0,0). Allocates a temporary pixel array
        /// and calls SetPixels32, but does not call Apply. If you already have a buffer, use
        /// <see cref="ClearBuffer"/> followed by <see cref="FlushBuffer"/> instead.
        /// </summary>
        public static void ClearTexture(Texture2D texture)
        {
            Color32[] clearPixels = new Color32[texture.width * texture.height];
            texture.SetPixels32(clearPixels);
        }

        /// <summary>
        /// Draws a line with thickness on a texture using Bresenham's algorithm. Calls SetPixel
        /// for each point with a per-pixel bounds check. Does not call Apply.
        /// When drawing multiple lines before a single upload, use <see cref="DrawLineToBuffer"/> instead.
        /// </summary>
        public static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, int thickness, Color color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
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

        /// <summary>
        /// Draws a horizontal line with thickness on a texture. Calls SetPixel per pixel with a
        /// per-pixel bounds check. Does not call Apply.
        /// When drawing multiple lines before a single upload, use <see cref="DrawHorizontalLineToBuffer"/> instead.
        /// </summary>
        public static void DrawHorizontalLine(Texture2D texture, int x, int y, int length, int thickness, Color color)
        {
            for (int i = 0; i < length; i++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    int px = x + i;
                    int py = y + t;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                        texture.SetPixel(px, py, color);
                }
            }
        }

        /// <summary>
        /// Draws a vertical line with thickness on a texture. Calls SetPixel per pixel with a
        /// per-pixel bounds check. Does not call Apply.
        /// When drawing multiple lines before a single upload, use <see cref="DrawVerticalLineToBuffer"/> instead.
        /// </summary>
        public static void DrawVerticalLine(Texture2D texture, int x, int y, int length, int thickness, Color color)
        {
            for (int i = 0; i < length; i++)
            {
                for (int t = 0; t < thickness; t++)
                {
                    int px = x + t;
                    int py = y + i;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                        texture.SetPixel(px, py, color);
                }
            }
        }

        /// <summary>
        /// Draws a filled circle on a texture. Calls SetPixel per pixel with a per-pixel bounds check.
        /// Does not call Apply.
        /// When drawing multiple shapes before a single upload, use <see cref="DrawCircleToBuffer"/> instead.
        /// </summary>
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

        /// <summary>
        /// Draws a filled circle directly into a pre-allocated <see cref="Color32"/> pixel buffer.
        /// Bounds are pre-clamped outside the inner loop to avoid per-pixel branching, making
        /// this significantly faster than <see cref="DrawCircle"/> when drawing many circles
        /// before a single <c>Texture2D.Apply()</c> call.
        /// </summary>
        /// <param name="buffer">Pixel buffer to write into. Must have length bufferWidth * bufferHeight.</param>
        /// <param name="bufferWidth">Width of the buffer in pixels.</param>
        /// <param name="bufferHeight">Height of the buffer in pixels.</param>
        /// <param name="cx">Circle center X in pixel coordinates.</param>
        /// <param name="cy">Circle center Y in pixel coordinates.</param>
        /// <param name="radius">Circle radius in pixels.</param>
        /// <param name="color">Fill color.</param>
        public static void DrawCircleToBuffer(Color32[] buffer, int bufferWidth, int bufferHeight, int cx, int cy, int radius, Color32 color)
        {
            int radiusSq = radius * radius;

            int pyMin = Mathf.Max(cy - radius, 0);
            int pyMax = Mathf.Min(cy + radius, bufferHeight - 1);
            int pxMin = Mathf.Max(cx - radius, 0);
            int pxMax = Mathf.Min(cx + radius, bufferWidth - 1);

            for (int py = pyMin; py <= pyMax; py++)
            {
                int dy = py - cy;
                int rowOffset = py * bufferWidth;

                for (int px = pxMin; px <= pxMax; px++)
                {
                    int dx = px - cx;
                    if (dx * dx + dy * dy <= radiusSq)
                        buffer[rowOffset + px] = color;
                }
            }
        }

        /// <summary>
        /// Draws a filled, heading-aware triangle directly into a pre-allocated <see cref="Color32"/>
        /// pixel buffer. The tip points in the direction given by <paramref name="headingDegrees"/>
        /// (0° = up/north, 90° = right/east clockwise, matching Unity's Y-axis rotation convention).
        /// The triangle is inscribed in a (2×<paramref name="halfWidth"/>) × (2×<paramref name="halfHeight"/>)
        /// bounding box centered on (<paramref name="cx"/>, <paramref name="cy"/>), where y=0 is the
        /// bottom of the buffer (Unity <c>SetPixels32</c> convention).
        /// </summary>
        public static void DrawTriangleToBuffer(Color32[] buffer, int bufferWidth, int bufferHeight,
            int cx, int cy, int halfWidth, int halfHeight, float headingDegrees, Color32 color)
        {
            float rad = headingDegrees * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(rad);

            // Local vertices (tip up, y=0 at bottom):
            //   tip   = ( 0,         +halfHeight)
            //   left  = (-halfWidth, -halfHeight)
            //   right = (+halfWidth, -halfHeight)
            // CW rotation by headingDegrees: x' = x*cos + y*sin,  y' = -x*sin + y*cos
            float tipX = cx + halfHeight * sinA;
            float tipY = cy + halfHeight * cosA;
            float leftX = cx + (-halfWidth * cosA - halfHeight * sinA);
            float leftY = cy + (halfWidth * sinA - halfHeight * cosA);
            float rightX = cx + (halfWidth * cosA - halfHeight * sinA);
            float rightY = cy + (-halfWidth * sinA - halfHeight * cosA);

            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(tipX, Mathf.Min(leftX, rightX))));
            int maxX = Mathf.Min(bufferWidth - 1, Mathf.CeilToInt(Mathf.Max(tipX, Mathf.Max(leftX, rightX))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(tipY, Mathf.Min(leftY, rightY))));
            int maxY = Mathf.Min(bufferHeight - 1, Mathf.CeilToInt(Mathf.Max(tipY, Mathf.Max(leftY, rightY))));

            for (int py = minY; py <= maxY; py++)
            {
                int rowOffset = py * bufferWidth;
                float fpy = py;

                for (int px = minX; px <= maxX; px++)
                {
                    float fpx = px;
                    // Edge functions (cross products). Point is inside when all three
                    // have the same sign (or zero), i.e., no mix of positive and negative.
                    float d1 = (leftX - tipX) * (fpy - tipY) - (leftY - tipY) * (fpx - tipX);
                    float d2 = (rightX - leftX) * (fpy - leftY) - (rightY - leftY) * (fpx - leftX);
                    float d3 = (tipX - rightX) * (fpy - rightY) - (tipY - rightY) * (fpx - rightX);

                    bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
                    bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
                    if (!(hasNeg && hasPos))
                        buffer[rowOffset + px] = color;
                }
            }
        }

        /// <summary>
        /// Clears a pixel buffer to fully transparent (0,0,0,0).
        /// Does not upload to GPU. Call <see cref="FlushBuffer"/> afterward to upload.
        /// </summary>
        public static void ClearBuffer(Color32[] buffer)
        {
            System.Array.Clear(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Fills every pixel in a buffer with the given color.
        /// Does not upload to GPU. Call <see cref="FlushBuffer"/> afterward to upload.
        /// </summary>
        public static void FillBuffer(Color32[] buffer, Color32 color)
        {
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = color;
        }

        /// <summary>
        /// Uploads a pre-allocated pixel buffer to a Texture2D without recalculating mipmaps.
        /// Unity docs warn that <c>Apply()</c> is expensive because it copies all pixels even if
        /// only a few changed, so callers should batch all buffer writes before calling this once.
        /// Passing <c>false</c> skips mip recalculation for textures created without a mip chain.
        /// </summary>
        public static void FlushBuffer(Texture2D texture, Color32[] buffer)
        {
            texture.SetPixels32(buffer);
            texture.Apply(false);
        }

        /// <summary>
        /// Draws a filled horizontal span into a pre-allocated pixel buffer.
        /// Bounds are pre-clamped outside the inner loop to avoid per-pixel branching.
        /// </summary>
        public static void DrawHorizontalLineToBuffer(Color32[] buffer, int bufferWidth, int bufferHeight,
            int x, int y, int length, int thickness, Color32 color)
        {
            int xStart = Mathf.Max(x, 0);
            int xEnd = Mathf.Min(x + length, bufferWidth);
            int yStart = Mathf.Max(y, 0);
            int yEnd = Mathf.Min(y + thickness, bufferHeight);
            for (int py = yStart; py < yEnd; py++)
            {
                int rowOffset = py * bufferWidth;
                for (int px = xStart; px < xEnd; px++)
                    buffer[rowOffset + px] = color;
            }
        }

        /// <summary>
        /// Draws a filled vertical span into a pre-allocated pixel buffer.
        /// Bounds are pre-clamped outside the inner loop to avoid per-pixel branching.
        /// </summary>
        public static void DrawVerticalLineToBuffer(Color32[] buffer, int bufferWidth, int bufferHeight,
            int x, int y, int length, int thickness, Color32 color)
        {
            int xStart = Mathf.Max(x, 0);
            int xEnd = Mathf.Min(x + thickness, bufferWidth);
            int yStart = Mathf.Max(y, 0);
            int yEnd = Mathf.Min(y + length, bufferHeight);
            for (int py = yStart; py < yEnd; py++)
            {
                int rowOffset = py * bufferWidth;
                for (int px = xStart; px < xEnd; px++)
                    buffer[rowOffset + px] = color;
            }
        }

        /// <summary>
        /// Draws a line with thickness into a pre-allocated pixel buffer using Bresenham's algorithm.
        /// Per-point bounding boxes are pre-clamped to avoid per-pixel branching.
        /// </summary>
        public static void DrawLineToBuffer(Color32[] buffer, int bufferWidth, int bufferHeight,
            int x0, int y0, int x1, int y1, int thickness, Color32 color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int half = thickness / 2;
            while (true)
            {
                int txMin = Mathf.Max(x0 - half, 0);
                int txMax = Mathf.Min(x0 + half, bufferWidth - 1);
                int tyMin = Mathf.Max(y0 - half, 0);
                int tyMax = Mathf.Min(y0 + half, bufferHeight - 1);
                for (int ty = tyMin; ty <= tyMax; ty++)
                {
                    int rowOffset = ty * bufferWidth;
                    for (int tx = txMin; tx <= txMax; tx++)
                        buffer[rowOffset + tx] = color;
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}