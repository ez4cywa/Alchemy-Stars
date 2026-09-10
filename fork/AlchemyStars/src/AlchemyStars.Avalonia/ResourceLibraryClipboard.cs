using System.Text.Json;

namespace AlchemyStars.Avalonia;

internal static class ResourceLibraryClipboard
{
    private const string Prefix = "alchemy-stars-resource-v1:";

    public static string Create(WorkspaceAnimation animation) => CreatePayload("animation", animation);
    public static string Create(WorkspacePart part) => CreatePayload("part", part);

    public static bool TryRead(string? text, out WorkspaceAnimation? animation, out WorkspacePart? part)
    {
        animation = null;
        part = null;
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        try
        {
            var payload = JsonSerializer.Deserialize(text[Prefix.Length..], ResourceClipboardJsonContext.Default.ClipboardPayload);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Kind) || string.IsNullOrWhiteSpace(payload.Json)) return false;
            if (payload.Kind.Equals("animation", StringComparison.OrdinalIgnoreCase))
                animation = JsonSerializer.Deserialize(payload.Json, ResourceClipboardJsonContext.Default.WorkspaceAnimation);
            else if (payload.Kind.Equals("part", StringComparison.OrdinalIgnoreCase))
                part = JsonSerializer.Deserialize(payload.Json, ResourceClipboardJsonContext.Default.WorkspacePart);
            return animation is not null || part is not null;
        }
        catch (JsonException) { return false; }
    }

    private static string CreatePayload(string kind, WorkspaceAnimation value) => Prefix + JsonSerializer.Serialize(
        new ClipboardPayload(kind, JsonSerializer.Serialize(value, ResourceClipboardJsonContext.Default.WorkspaceAnimation)),
        ResourceClipboardJsonContext.Default.ClipboardPayload);
    private static string CreatePayload(string kind, WorkspacePart value) => Prefix + JsonSerializer.Serialize(
        new ClipboardPayload(kind, JsonSerializer.Serialize(value, ResourceClipboardJsonContext.Default.WorkspacePart)),
        ResourceClipboardJsonContext.Default.ClipboardPayload);
}

internal sealed record ClipboardPayload(string Kind, string Json);

[System.Text.Json.Serialization.JsonSerializable(typeof(ClipboardPayload))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WorkspaceAnimation))]
[System.Text.Json.Serialization.JsonSerializable(typeof(WorkspacePart))]
internal partial class ResourceClipboardJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
