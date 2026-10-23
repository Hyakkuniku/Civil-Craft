int checks = 0;
void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
foreach ((float width, float height, float scale) in new[] { (1280f, 720f, .67f), (1600f, 1200f, .84f), (2340f, 1080f, 1f) })
foreach ((float tx, float ty, float tw, float th) in new[] { (110f, 65f, 150f, 90f), (1100f, 65f, 70f, 70f), (1100f, 650f, 70f, 70f), (85f, 650f, 70f, 70f), (640f, 675f, 80f, 60f), (640f, 360f, 300f, 50f) })
foreach ((float ox, float oy, float rotation) in new[] { (0f, 20f, 180f), (0f, -30f, 0f), (-90f, 0f, -90f), (0f, 0f, 0f) })
{
    float x = tx / 1280f * width, y = ty / 720f * height;
    float aw = 80 * scale, ah = 80 * scale, bounce = 15 * scale;
    var p = BuildTutorialPointerLayout.Resolve(x, y, tw * scale, th * scale, aw, ah, ox * scale, oy * scale, rotation,
        bounce, 10 * scale, 8 * scale, 8 * scale, width - 8 * scale, height - 8 * scale);
    bool vertical = p.rotation == 0 || p.rotation == 180;
    float halfW = (vertical ? aw : ah) * .5f, halfH = (vertical ? ah : aw) * .5f;
    for (int sample = -1; sample <= 1; sample++)
    {
        float bx = vertical ? 0 : sample * bounce, by = vertical ? sample * bounce : 0;
        Check(p.x + bx - halfW >= 8 * scale - .01f && p.x + bx + halfW <= width - 8 * scale + .01f &&
            p.y + by - halfH >= 8 * scale - .01f && p.y + by + halfH <= height - 8 * scale + .01f,
            "Entire arrow and bounce must remain within the safe area.");
        bool separated = Math.Abs(p.x + bx - x) >= (tw * scale + halfW * 2) * .5f + 9 * scale ||
                         Math.Abs(p.y + by - y) >= (th * scale + halfH * 2) * .5f + 9 * scale;
        Check(separated, "Build arrow must remain outside the target even at closest bounce.");
    }
}
var undo = BuildTutorialPointerLayout.Resolve(1100, 650, 80, 80, 80, 80, 0, 0, 0, 15, 10, 8, 8, 1272, 712);
Check(undo.y < 650 && undo.rotation == 0, "Zero-offset Undo should be below its top-dock button, not centered on it.");
var bottom = BuildTutorialPointerLayout.Resolve(640, 45, 100, 70, 80, 80, 0, -30, 0, 15, 10, 8, 8, 1272, 712);
Check(bottom.y > 45 && bottom.rotation == 180, "Out-of-bounds bottom preference must flip above the target.");
Console.WriteLine($"PASS: {checks} production pixel-placement checks across phone/tablet/wide screens, toolbar corners, zero Undo offset and full bounce envelopes. No Unity scenes or saves accessed.");
