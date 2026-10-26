namespace MiIALocal.Core;

public static class VisualAnimation
{
    public const string NegativePrompt = "oversaturated colors, overexposure, blurry details, low quality, compression artifacts, frozen frame, severe warping, melting geometry, morphing objects, flicker, duplicate objects, deformed limbs, subtitles, watermarks";
    public static string Prompt(string original, bool hasReference) => hasReference
        ? "Animate the provided starting image. Preserve the subjects' identity, shapes, colors, visual style and scene throughout the motion. Follow this motion request: " + original
        : original;
    public static (int Width, int Height) Dimensions(int format, bool highDetail = false) => highDetail
        ? format switch { 1 => (832, 480), 2 => (480, 832), _ => (672, 672) }
        : format switch { 1 => (768, 448), 2 => (448, 768), _ => (576, 576) };

    // Wan I2V accepts multiples of 16. Match the reference instead of manufacturing
    // repeated border pixels; keep a bounded pixel budget for the desktop profiles.
    public static (int Width, int Height) ForReference(int width, int height, bool highDetail = false)
    {
        if (width < 1 || height < 1) return Dimensions(0, highDetail);
        var ratio = (double)width / height;
        var budget = highDetail ? 460800 : 331776;
        var best = Dimensions(0, highDetail); var score = double.MaxValue;
        for (var w = 256; w <= 832; w += 16)
        for (var h = 256; h <= 832; h += 16)
        {
            var pixels = w * h; if (pixels > budget) continue;
            var candidate = 4 * Math.Abs(Math.Log((double)w / h / ratio)) + 0.5 * Math.Log((double)budget / pixels);
            if (candidate < score) { score = candidate; best = (w, h); }
        }
        return best;
    }
}
