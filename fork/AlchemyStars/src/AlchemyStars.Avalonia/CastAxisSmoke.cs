using Cast.NET;
using Cast.NET.Nodes;
using System.Numerics;

namespace AlchemyStars.Avalonia;

internal static class CastAxisSmoke
{
    internal static void Run(string directory)
    {
        var folder = Path.Combine(directory, "axis");
        Directory.CreateDirectory(folder);
        foreach (var axis in new string?[] { "y", "z", null })
        {
            var source = Path.Combine(folder, (axis ?? "missing") + ".cast");
            Write(source, axis);
            var original = File.ReadAllBytes(source);
            var request = Request(source, folder, "out-" + (axis ?? "missing"));
            var output = new AnimationExportEngine().Export(request).OutputFiles.Single();
            var metadata = CastReader.Load(output).RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>().SingleOrDefault();
            Require(metadata?.UpAxis == (axis ?? "y"), "Packaged CAST lost its up axis: " + axis);
            Require(original.SequenceEqual(File.ReadAllBytes(source)), "Axis export changed the input.");
        }
        var y = Path.Combine(folder, "y.cast");
        var z = Path.Combine(folder, "z.cast");
        var invalid = Path.Combine(folder, "invalid.cast");
        Write(invalid, "x");
        Reject(Request(invalid, folder, "invalid-output"));
        Reject(Request(y, folder, "mixed-output") with
        {
            Parts = [new(y, ModelPartKind.ViewHands), new(z, ModelPartKind.Weapon, "tag_origin")]
        });
        Console.WriteLine("CAST axis: Y/Z preservation, missing=Y, invalid/mixed rejection, immutable inputs PASS");
    }

    private static AnimationExportRequest Request(string source, string folder, string name) => new(
        [new(source, ModelPartKind.ViewHands)],
        [new(source, name, folder, EnableLeftHandIk: false, EnableRightHandIk: false)],
        new(new("", "", "", ""), new("", "", "", "")));

    private static void Reject(AnimationExportRequest request)
    {
        try { new AnimationExportEngine().Export(request); }
        catch (InvalidDataException)
        {
            Require(!File.Exists(Path.Combine(request.Animations[0].OutputFolder, request.Animations[0].OutputName + ".cast")),
                "Rejected axis left an output.");
            return;
        }
        throw new InvalidOperationException("Invalid/mixed CAST up axes were accepted.");
    }

    private static void Write(string path, string? axis)
    {
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = 1 };
        if (axis is not null)
        {
            var metadata = new CastNode(CastNodeIdentifier.Metadata) { Hash = 2, Parent = root };
            metadata.AddString("up", axis);
        }
        var model = new ModelNode { Hash = 3, Parent = root };
        var skeleton = new SkeletonNode { Hash = 4, Parent = model };
        var bone = new BoneNode { Hash = 5, Parent = skeleton };
        bone.AddString("n", "tag_origin");
        bone.AddValue("p", uint.MaxValue);
        bone.AddValue("lp", new Vector3(1, 2, 3));
        bone.AddValue("wp", new Vector3(1, 2, 3));
        bone.AddValue("lr", new Vector4(0, 0, 0, 1));
        bone.AddValue("wr", new Vector4(0, 0, 0, 1));
        var animation = new AnimationNode { Hash = 6, Parent = root };
        animation.AddValue("fr", 30f);
        ulong hash = 7;
        foreach (var (channel, values) in new[] { ("tx", new float[] { 1, 2 }), ("ty", new float[] { 2, 2 }), ("tz", new float[] { 3, 3 }) })
        {
            _ = new CurveNode
            {
                Hash = hash++, Parent = animation, NodeName = "tag_origin", KeyPropertyName = channel, Mode = "absolute",
                KeyFrameBuffer = new CastArrayProperty<byte>([0, 1]), KeyValueBuffer = new CastArrayProperty<float>(values),
            };
        }
        _ = new CurveNode
        {
            Hash = hash, Parent = animation, NodeName = "tag_origin", KeyPropertyName = "rq", Mode = "absolute",
            KeyFrameBuffer = new CastArrayProperty<byte>([0, 1]),
            KeyValueBuffer = new CastArrayProperty<Vector4>([new(0, 0, 0, 1), new(0, 0, 0, 1)]),
        };
        CastWriter.Save(path, new Cast.NET.Cast([root]));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
