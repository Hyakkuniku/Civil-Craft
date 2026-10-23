using System;

/// <summary>Pixel-space placement only; no input, tutorial state or scene dependencies.</summary>
public static class BuildTutorialPointerLayout
{
    public struct Placement
    {
        public float x, y, rotation;
        public Placement(float x, float y, float rotation) { this.x = x; this.y = y; this.rotation = rotation; }
    }

    public static Placement Resolve(float targetX, float targetY, float targetWidth, float targetHeight,
        float arrowWidth, float arrowHeight, float offsetX, float offsetY, float preferredRotation,
        float bounce, float gap, float minX, float minY, float maxX, float maxY)
    {
        // Direction points OUT from the control; the arrow points back toward it.
        int preferred;
        if (Math.Abs(offsetX) + Math.Abs(offsetY) > .01f)
            preferred = Math.Abs(offsetX) > Math.Abs(offsetY) ? (offsetX < 0 ? 2 : 3) : (offsetY < 0 ? 1 : 0);
        else
        {
            double radians = preferredRotation * Math.PI / 180;
            double x = Math.Sin(radians), y = -Math.Cos(radians);
            preferred = Math.Abs(x) > Math.Abs(y) ? (x < 0 ? 2 : 3) : (y < 0 ? 1 : 0);
        }
        float requested = (float)Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
        Placement best = default;
        double bestCost = double.MaxValue;
        for (int side = 0; side < 4; side++)
        {
            bool vertical = side < 2;
            float width = vertical ? arrowWidth : arrowHeight, height = vertical ? arrowHeight : arrowWidth;
            float distance = (vertical ? targetHeight + height : targetWidth + width) * .5f + Math.Abs(bounce) + Math.Max(0, gap);
            if (side == preferred) distance = Math.Max(distance, requested);
            float x = targetX + (side == 2 ? -distance : side == 3 ? distance : 0);
            float y = targetY + (side == 1 ? -distance : side == 0 ? distance : 0);
            float marginX = width * .5f + (vertical ? 0 : Math.Abs(bounce));
            float marginY = height * .5f + (vertical ? Math.Abs(bounce) : 0);
            double overflow = Math.Max(0, minX + marginX - x) + Math.Max(0, x + marginX - maxX) +
                              Math.Max(0, minY + marginY - y) + Math.Max(0, y + marginY - maxY);
            double cost = overflow * 10000 + (side == preferred ? 0 : 1);
            if (cost >= bestCost) continue;
            bestCost = cost;
            best = new Placement(x, y, side == 0 ? 180 : side == 1 ? 0 : side == 2 ? -90 : 90);
        }
        // Last-resort bounds for an unusually large target close to a corner.
        bool upright = best.rotation == 0 || best.rotation == 180;
        float halfW = (upright ? arrowWidth : arrowHeight) * .5f + (upright ? 0 : Math.Abs(bounce));
        float halfH = (upright ? arrowHeight : arrowWidth) * .5f + (upright ? Math.Abs(bounce) : 0);
        best.x = Clamp(best.x, minX + halfW, maxX - halfW);
        best.y = Clamp(best.y, minY + halfH, maxY - halfH);
        return best;
    }

    private static float Clamp(float value, float min, float max) => min > max ? (min + max) * .5f : Math.Max(min, Math.Min(max, value));
}
