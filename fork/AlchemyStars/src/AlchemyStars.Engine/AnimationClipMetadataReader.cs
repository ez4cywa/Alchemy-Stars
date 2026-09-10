using Cast.NET;
using Cast.NET.Nodes;

namespace AlchemyStars.Engine;

public readonly record struct AnimationClipMetadata(
    int FirstFrame,
    int LastFrame,
    int FrameCount,
    float Framerate);

/// <summary>
/// Reads the timing information needed by the editor without converting or
/// modifying the source CAST file.
/// </summary>
public static class AnimationClipMetadataReader
{
    public static AnimationClipMetadata Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var cast = CastReader.Load(filePath);
        var animations = cast.RootNodes
            .SelectMany(DescendantsAndSelf)
            .OfType<AnimationNode>()
            .ToArray();
        if (animations.Length != 1)
            throw new InvalidDataException($"Expected exactly one animation in '{filePath}', found {animations.Length}.");

        var animation = animations[0];
        var keyframes = animation.EnumerateCurves()
            .SelectMany(curve => curve.EnumerateKeyFrames())
            .ToArray();
        var firstFrame = keyframes.Length == 0 ? 0 : (int)MathF.Floor(keyframes.Min());
        var lastFrame = keyframes.Length == 0 ? 0 : (int)MathF.Ceiling(keyframes.Max());
        var frameCount = Math.Max(1, lastFrame - firstFrame + 1);
        var framerate = float.IsFinite(animation.Framerate) && animation.Framerate > 0
            ? animation.Framerate
            : 30f;
        if (!float.IsFinite(animation.Framerate) || animation.Framerate <= 0)
            throw new InvalidDataException("Invalid source animation framerate: " + filePath);
        var normalizedLast = (int)MathF.Ceiling(Math.Max(0, lastFrame) * 30f / framerate);
        return new AnimationClipMetadata(0, normalizedLast, normalizedLast + 1, 30f);
    }

    private static IEnumerable<CastNode> DescendantsAndSelf(CastNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
        }
    }
}
