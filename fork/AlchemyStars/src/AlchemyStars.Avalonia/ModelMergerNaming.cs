namespace AlchemyStars.Avalonia;

/// <summary>
/// Weapon-code output naming shared by merging, ammunition filling and arm assembly,
/// mirroring the vendored engine's <c>weapon_code</c>. When the derived name already
/// exists in the output folder the differing segments of the input part names become
/// a prefix, and a numeric ladder is the final fallback, so automatic names never
/// overwrite existing files.
/// </summary>
public static class ModelMergerNaming
{
    private static readonly string[] PartTypes =
        ["rec", "barl", "mag", "bolt", "grip", "muz", "muzzle", "stck", "stock", "hand", "charge", "tube", "body", "trig"];

    /// <summary>Derives the weapon code, e.g. att_sat_vm_ar_eagle_rec_LOD0 → eagle.</summary>
    public static string WeaponCode(string stem)
    {
        var segments = new List<string>(stem.Split('_'));
        if (segments.Count > 0 && IsLodSegment(segments[^1])) segments.RemoveAt(segments.Count - 1);
        while (segments.Count > 0 && (IsNumeric(segments[^1]) || IsVersion(segments[^1]))) segments.RemoveAt(segments.Count - 1);
        for (var index = segments.Count - 1; index >= 1; index--)
            if (PartTypes.Contains(segments[index], StringComparer.OrdinalIgnoreCase))
                return segments[index - 1];
        return string.Join("_", segments);
    }

    /// <summary>
    /// Segments that vary across the input part stems (part types, variants), excluding
    /// LOD, numeric, version and the code itself. Empty when every segment is shared.
    /// </summary>
    public static string Distinguisher(IReadOnlyList<string> stems, string code)
    {
        if (stems.Count == 0) return "";
        var parsed = stems.Select(stem => stem.Split('_')
            .Where(segment => !IsLodSegment(segment) && !IsNumeric(segment) && !IsVersion(segment))
            .ToArray()).ToArray();
        var shared = parsed[0].Where(segment => parsed.All(parts => parts.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        shared.Add(code);
        var chosen = new List<string>();
        foreach (var parts in parsed)
            foreach (var segment in parts)
                if (!shared.Contains(segment) && !chosen.Contains(segment, StringComparer.OrdinalIgnoreCase))
                {
                    chosen.Add(segment);
                    if (chosen.Count == 4) return string.Join("_", chosen);
                }
        return string.Join("_", chosen);
    }

    /// <summary>
    /// Resolves a unique file name inside the folder: the base name when free,
    /// then the distinguisher prefix, then _2, _3… increments, so the returned
    /// name is always free.
    /// </summary>
    public static string UniqueFileName(string directory, string baseName, string distinguisher = "")
    {
        var stem = Path.GetFileNameWithoutExtension(baseName);
        var extension = Path.GetExtension(baseName);
        if (!Exists(directory, baseName)) return baseName;
        if (!string.IsNullOrEmpty(distinguisher))
        {
            stem = $"{distinguisher}_{stem}";
            if (!Exists(directory, $"{stem}{extension}")) return $"{stem}{extension}";
        }
        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{stem}_{index}{extension}";
            if (!Exists(directory, candidate)) return candidate;
        }
        return $"{stem}_{Guid.NewGuid():N}{extension}";
    }

    /// <summary>Default merge name: the weapon code, prefixed on conflicts.</summary>
    public static string MergeOutputName(string folder, IReadOnlyList<string> parts)
    {
        var stem = Path.GetFileNameWithoutExtension(parts.Count > 0 ? parts[0] : "");
        var code = WeaponCode(stem);
        if (code.Length == 0) code = stem;
        return UniqueFileName(folder, $"{code}.cast", Distinguisher(parts.Select(p => Path.GetFileNameWithoutExtension(p) ?? "").ToList(), code));
    }

    /// <summary>Default fill name: the weapon code with the filled suffix.</summary>
    public static string FillOutputName(string folder, string weaponPath)
    {
        var code = WeaponCode(Path.GetFileNameWithoutExtension(weaponPath));
        if (code.Length == 0) code = Path.GetFileNameWithoutExtension(weaponPath);
        return UniqueFileName(folder, $"{code}_filled.cast");
    }

    /// <summary>Default arm-assembly name: the weapon code with the viewhands suffix.</summary>
    public static string AssemblyOutputName(string folder, string weaponPath)
    {
        var code = WeaponCode(Path.GetFileNameWithoutExtension(weaponPath));
        if (code.Length == 0) code = Path.GetFileNameWithoutExtension(weaponPath);
        return UniqueFileName(folder, $"{code}_viewhands.cast");
    }

    private static bool Exists(string directory, string fileName) =>
        !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, fileName));

    private static bool IsLodSegment(string segment) =>
        segment.Length > 3 && segment.StartsWith("lod", StringComparison.OrdinalIgnoreCase)
        && segment[3..].All(char.IsAsciiDigit);

    private static bool IsNumeric(string segment) => segment.Length > 0 && segment.All(char.IsAsciiDigit);

    private static bool IsVersion(string segment) =>
        segment.Length > 1 && (segment[0] is 'v' or 'V') && segment[1..].All(char.IsAsciiDigit);
}
