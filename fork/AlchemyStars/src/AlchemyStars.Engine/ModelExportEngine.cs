using Alchemist.UI;
using RedFox.Graphics3D;

namespace AlchemyStars.Engine;

public sealed record ModelExportRequest(
    IReadOnlyList<ModelPartSpec> Parts,
    string OutputDirectory,
    ExportFormat Format = ExportFormat.Cast,
    bool MatchOldCallOfDuty = false,
    string OutputUpAxis = "source");

public sealed record ModelExportResult(string OutputFile, int BoneCount, int MeshCount);

/// <summary>Exports the model parts with the same skeleton merge and skin remapping used by animation export.</summary>
public sealed class ModelExportEngine
{
    public ModelExportResult Export(ModelExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Parts is null || request.Parts.Count == 0)
            throw new ExportValidationException(ExportErrorCode.NoModelParts, "At least one model part is required.");

        var hands = request.Parts.Where(part => part.Kind == ModelPartKind.ViewHands).ToArray();
        var weapons = request.Parts.Where(part => part.Kind == ModelPartKind.Weapon).ToArray();
        if (hands.Length == 0)
            throw new InvalidDataException("模型导出需要手臂模型 / Model export requires view hands.");
        if (weapons.Length == 0)
            throw new InvalidDataException("模型导出需要武器模型 / Model export requires a weapon.");
        foreach (var part in request.Parts)
            if (string.IsNullOrWhiteSpace(part.FilePath) || !File.Exists(part.FilePath))
                throw new FileNotFoundException("模型部件不存在 / Model part is missing.", part.FilePath);

        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var extension = GetModelExtension(request.Format);
        var weaponName = Path.GetFileNameWithoutExtension(weapons[0].FilePath);
        if (string.IsNullOrWhiteSpace(weaponName))
            throw new InvalidDataException("无法从武器文件取得输出名称 / The weapon file has no usable name.");
        var output = Path.GetFullPath(Path.Combine(outputDirectory, weaponName + "_model" + extension));
        if (request.Parts.Select(part => Path.GetFullPath(part.FilePath)).Contains(output, StringComparer.OrdinalIgnoreCase))
            throw new ExportValidationException(ExportErrorCode.OutputWouldOverwriteInput,
                "输出会覆盖输入素材 / Output would overwrite an input: " + output);

        var compatibilityParts = request.Parts.Select(AnimationExportEngine.ToCompatibilityPart).ToArray();
        var plan = SkeletonMergePlan.Build(compatibilityParts, request.MatchOldCallOfDuty, outputAxis: request.OutputUpAxis);
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "AlchemyStarsModelExport", Guid.NewGuid().ToString("N"));
        var mergedCast = request.Format == ExportFormat.Cast ? output : Path.Combine(stagingDirectory, "merged.cast");
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            MayaCastPackage.SaveModel(mergedCast, plan, AnimationConverter.TranslatorFactory);
            var model = AnimationConverter.TranslatorFactory.Load<Model>(mergedCast);
            switch (request.Format)
            {
                case ExportFormat.Fbx:
                    DesktopFbxExporter.Export(mergedCast, output, WorkspacePaths.StandardAnimationFramerate);
                    break;
                case ExportFormat.Smd:
                    SaveAtomically(output, temporary => SmdModelExporter.Save(temporary, mergedCast, plan.Skeleton));
                    break;
                case ExportFormat.Seanim:
                    PrepareSeModel(model);
                    SaveAtomically(output, temporary => AnimationConverter.TranslatorFactory.Save(temporary, model));
                    break;
            }
            return new(output, plan.Skeleton.Bones.Count, model.Meshes.Count);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                try { Directory.Delete(stagingDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
        }
    }

    public static string GetModelExtension(ExportFormat animationFormat) => animationFormat switch
    {
        ExportFormat.Fbx => ".fbx",
        ExportFormat.Smd => ".smd",
        ExportFormat.Seanim => ".semodel",
        _ => ".cast",
    };

    private static void PrepareSeModel(Model model)
    {
        if (model.Materials.Count == 0)
            model.Materials.Add(new Material("material"));
        foreach (var mesh in model.Meshes)
        {
            var layerCount = mesh.UVLayers is { Count: > 0 } layers ? layers.Dimension : 1;
            while (mesh.Materials.Count < layerCount)
                mesh.Materials.Add(mesh.Materials.Count == 0 ? model.Materials[0] : mesh.Materials[^1]);
            while (mesh.Materials.Count > layerCount)
                mesh.Materials.RemoveAt(mesh.Materials.Count - 1);
        }
    }

    private static void SaveAtomically(string outputPath, Action<string> write)
    {
        var extension = Path.GetExtension(outputPath);
        var temporary = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.tmp{extension}");
        try
        {
            write(temporary);
            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
