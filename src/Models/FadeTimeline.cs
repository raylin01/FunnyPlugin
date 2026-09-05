namespace Funnies.Models;

public static class FadeTimeline
{
    public static int Alpha(float now, float start, float end)
    {
        if (end <= start || now >= end) return 0;
        var midpoint = start + (end - start) * 0.5f;
        if (now <= midpoint) return 255;
        return (int)Math.Clamp(255f * (end - now) / (end - midpoint), 0, 255);
    }
}
