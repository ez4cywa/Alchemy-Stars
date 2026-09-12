using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;

namespace AlchemyStars.Avalonia;

internal static partial class SelfTest
{
    public static int RunModelExport(IReadOnlyList<string> arguments)
    {
        try
        {
            Require(arguments.Count == 4, "Model export smoke requires: hands CAST, weapon CAST, output folder and animation format.");
            var hands = Path.GetFullPath(arguments[0]);
            var weapon = Path.GetFullPath(arguments[1]);
            var outputFolder = Path.GetFullPath(arguments[2]);
            var format = OutputFormats.ToExportFormat(arguments[3]);
            var result = new ModelExportEngine().Export(new(
                [new(hands, ModelPartKind.ViewHands), new(weapon, ModelPartKind.Weapon, "tag_weapon")],
                outputFolder,
                format));
            var expectedExtension = ModelExportEngine.GetModelExtension(format);
            Require(Path.GetFileName(result.OutputFile) == Path.GetFileNameWithoutExtension(weapon) + "_model" + expectedExtension,
                "Bound-model output naming is incorrect.");
            Require(File.Exists(result.OutputFile) && new FileInfo(result.OutputFile).Length > 0,
                "Bound-model output is missing or empty.");
            Require(result.BoneCount > 0 && result.MeshCount > 0, "Bound-model output contains no skeleton or meshes.");
            if (format == ExportFormat.Cast)
            {
                var nodes = CastReader.Load(result.OutputFile).RootNodes.SelectMany(Walk).ToArray();
                Require(nodes.OfType<ModelNode>().Count() == 1 && nodes.All(node => node is not AnimationNode),
                    "Bound-model CAST must contain one merged model and no animation.");
            }
            else if (format == ExportFormat.Smd)
            {
                Require(File.ReadLines(result.OutputFile).Any(line => line == "triangles"),
                    "Bound-model SMD has no triangle section.");
            }
            else if (format == ExportFormat.Seanim)
            {
                var model = new RedFox.Graphics3D.Translation.Graphics3DTranslatorFactory()
                    .WithDefaultTranslators().Load<Model>(result.OutputFile);
                Require(model.Meshes.Count == result.MeshCount && model.Skeleton?.Bones.Count == result.BoneCount,
                    "Bound SEModel could not be read back with its merged skeleton.");
            }
            Console.WriteLine(result.OutputFile);
            Console.WriteLine($"Bound model: PASS ({result.BoneCount} bones, {result.MeshCount} meshes, {expectedExtension})");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static IEnumerable<CastNode> Walk(CastNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }
}
