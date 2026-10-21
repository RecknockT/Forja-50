namespace MiIALocal.Core;

public static class VisualAnimation
{
    public const string NegativePrompt = "oversaturated colors, overexposure, blurry details, low quality, compression artifacts, frozen frame, severe warping, melting geometry, morphing objects, flicker, duplicate objects, deformed limbs, subtitles, watermarks";
    public static string Prompt(string original, bool hasReference) => hasReference
        ? "Animate the provided starting image. Preserve the subjects' identity, shapes, colors, visual style and scene throughout the motion. Follow this motion request: " + original
        : original;
    public static (int Width, int Height) Dimensions(int format) => format switch { 1 => (768, 448), 2 => (448, 768), _ => (576, 576) };
}
