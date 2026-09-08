using Cast.NET;
using Cast.NET.Nodes;
using System.Globalization;
using System.Numerics;

namespace AlchemyStars.Avalonia;

internal static class SelfTest
{
    public static int RunPreview(string path, string? projectPath = null)
    {
        try
        {
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
            var store = new WorkspaceProjectStore();
            var request = projectPath is null ? null : store.CreateExportRequest(store.Load(projectPath));
            var scene = CastPreviewScene.Load(path, request?.Parts, request?.Options.MatchOldCallOfDuty ?? false);
            Require(scene.FrameCount > 1, "Expected an animated CAST for the preview regression.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var first = CastPreviewRenderer.Render(scene, 0, 640, 400, PreviewCamera.Default, false);
            var another = CastPreviewRenderer.Render(scene, scene.FrameCount / 2, 640, 400, PreviewCamera.Default, false);
            var repeat = CastPreviewRenderer.Render(scene, 0, 640, 400, PreviewCamera.Default, false);
            var firstPerson = CastPreviewRenderer.Render(scene, 0, 640, 400, PreviewCamera.FirstPerson, false);
            var gpuFrame = CastPreviewRenderer.Prepare(scene, 0, 640, 400, PreviewCamera.FirstPerson, false);
            Require(gpuFrame.TriangleCount > 0 && gpuFrame.Width == 640 && gpuFrame.Height == 400,
                "GPU preview frame was not prepared.");
            Require(gpuFrame.TrianglePoints.Length == gpuFrame.TriangleColors.Length,
                "GPU preview vertex colors are incomplete.");
            Require(first.Count(pixel => pixel != CastPreviewRenderer.Background) > 50, "CAST geometry was not rasterized.");
            Require(!first.SequenceEqual(another), "Animation sampling did not change the preview.");
            Require(first.SequenceEqual(repeat), "Reverse frame scrubbing accumulated transforms.");
            Require(firstPerson.Count(pixel => pixel != CastPreviewRenderer.Background) > 50, "First-person CAST geometry was not rasterized.");
            var firstPersonGeometry = firstPerson.Select((pixel, index) => (pixel, index))
                .Where(item => item.pixel != CastPreviewRenderer.Background).Select(item => item.index).ToArray();
            var firstPersonBounds = new
            {
                Left = firstPersonGeometry.Min(index => index % 640),
                Top = firstPersonGeometry.Min(index => index / 640),
                Right = firstPersonGeometry.Max(index => index % 640),
                Bottom = firstPersonGeometry.Max(index => index / 640),
            };
            Require(firstPersonBounds.Left > 1 && firstPersonBounds.Top > 1 && firstPersonBounds.Right < 638 && firstPersonBounds.Bottom < 398,
                $"First-person weapon is clipped by the viewport: {firstPersonBounds}.");
            Require(first.Where(pixel => pixel != CastPreviewRenderer.Background).Distinct().Count() > 16,
                "Smooth clay shading did not produce enough tonal detail.");
            Require(CastPreviewRenderer.AntiAliasingSamples == 2, "CAST preview anti-aliasing is disabled.");
            Require(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), "Preview modified the CAST.");
            Console.WriteLine($"CAST preview: PASS ({scene.VertexCount} vertices, {scene.BoneCount} bones, {scene.FrameCount} frames; orbit and 90-degree first-person renders {watch.ElapsedMilliseconds} ms)");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    public static int Run()
    {
        try
        {
            var engine = new AnimationExportEngine();
            var capabilities = engine.Capabilities;
            Require(capabilities.Version == AnimationExportEngine.EngineVersion, "Engine version mismatch.");
            Require(Enum.GetValues<ExportFormat>().All(capabilities.OutputFormats.Contains), "An output format is missing.");
            Require(capabilities.SupportsAnimationOnlyCast, "Animation-only CAST is not advertised.");
            Require(capabilities.SupportsSelectiveBoneBake, "Selective bone baking is not advertised.");
            Require(capabilities.SupportsNativeAot, "Native AOT is not advertised.");
            var firstPersonView = CastPreviewRenderer.ResolveView(new CastPreviewScene(), 640, 400, PreviewCamera.FirstPerson);
            Require(Vector3.Distance(firstPersonView.Eye, Vector3.Zero) < 1e-5f, "First-person camera must use Maya's default origin.");
            Require(Vector3.Distance(firstPersonView.Forward, Vector3.UnitX) < 1e-5f, "Maya camera X/Z rotation did not produce forward +X.");
            Require(Vector3.Distance(firstPersonView.Right, -Vector3.UnitY) < 1e-5f && Vector3.Distance(firstPersonView.Up, Vector3.UnitZ) < 1e-5f,
                "Maya camera X/Z rotation produced an invalid view basis.");
            Require(MathF.Abs(firstPersonView.FocalLength - 320) < 1e-4f, "First-person horizontal FOV is not 90 degrees.");
            Require(MathF.Abs(firstPersonView.NearClip - 0.1f) < 1e-5f, "First-person camera does not use Maya's default near clip.");
            try
            {
                engine.Export(new AnimationExportRequest(
                    [],
                    [],
                    new AnimationExportOptions(
                        new IkChainSpec("a", "b", "c", "d"),
                        new IkChainSpec("a", "b", "c", "d"))));
                throw new InvalidOperationException("Empty export request was accepted.");
            }
            catch (ExportValidationException exception)
            {
                Require(exception.Code == ExportErrorCode.NoModelParts, "Structured export error code mismatch.");
            }

            var previousCulture = CultureInfo.CurrentUICulture;
            var testDirectory = Path.Combine(Path.GetTempPath(), $"AlchemyStars-AotSelfTest-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDirectory);
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("zh-CN");
                var preferences = new ApplicationPreferencesStore(Path.Combine(testDirectory, "settings.json"));
                Require(preferences.Snapshot().ThemeStyle == "apple" && preferences.Snapshot().ThemeMode == "light",
                    "Existing installations must retain the Apple light default.");
                preferences.SaveAppearance("classic-apple", "system");
                var classicAppearance = new ApplicationPreferencesStore(Path.Combine(testDirectory, "settings.json")).Snapshot();
                Require(classicAppearance.ThemeStyle == "classic-apple" && classicAppearance.ThemeMode == "system",
                    "Classic Apple appearance must persist across process reloads.");
                preferences.SaveAppearance("neumorphic", "dark");
                preferences.SaveLanguage("system");
                preferences.SaveDefaults("system", preferences.CreateWorkspace());
                var reloadedAppearance = new ApplicationPreferencesStore(Path.Combine(testDirectory, "settings.json")).Snapshot();
                Require(reloadedAppearance.ThemeStyle == "neumorphic" && reloadedAppearance.ThemeMode == "dark",
                    "Appearance must persist without being overwritten by other preference saves.");
                preferences.SaveAppearance("unknown", "unknown");
                Require(preferences.Snapshot().ThemeStyle == "apple" && preferences.Snapshot().ThemeMode == "light",
                    "Unknown appearance values must fall back safely.");
                var projectStore = new WorkspaceProjectStore();
                var filePicker = new SelfTestFilePicker();
                var viewModel = new MainWindowViewModel(engine, projectStore, preferences, filePicker);
                Require(viewModel.IsChinese, "Chinese system language was not detected.");
                Require(viewModel.Text.ProductName == "炼金之星", "Chinese product name mismatch.");
                Require(viewModel.Text.ImportAnimationsMenu == "导入动画…"
                    && viewModel.Text.ImportLayersMenu == "导入动画层…"
                    && viewModel.Text.ImportPartsMenu == "导入模型部件…"
                    && viewModel.Text.FitSubjectMenu == "适应主体 (F)", "Chinese context menus are not localized.");
                viewModel.ToggleLanguage();
                Require(!viewModel.IsChinese && viewModel.Text.ProductName == "Alchemy Stars", "Language toggle failed.");
                Require(viewModel.Text.ImportAnimationsMenu == "Import animations…"
                    && viewModel.Text.ImportLayersMenu == "Import animation layers…"
                    && viewModel.Text.ImportPartsMenu == "Import model parts…"
                    && viewModel.Text.FitSubjectMenu == "Fit subject (F)", "English context menus are not localized.");
                viewModel.OpenProjectRepositoryAsync().GetAwaiter().GetResult();
                Require(filePicker.LastOpenedUri?.AbsoluteUri == MainWindowViewModel.ProjectRepositoryUrl,
                    "About did not open the current project repository.");
                viewModel.OpenUpstreamAsync().GetAwaiter().GetResult();
                Require(filePicker.LastOpenedUri?.AbsoluteUri == "https://github.com/Scobalula/Alchemist",
                    "About did not open the upstream repository.");
                Require(viewModel.FooterStatus.Contains("github.com", StringComparison.Ordinal),
                    "About did not report a successful external-link handoff.");
                filePicker.OpenUriResult = false;
                viewModel.OpenProjectRepositoryAsync().GetAwaiter().GetResult();
                Require(viewModel.IsDialogOpen && viewModel.DialogMessage.Contains(MainWindowViewModel.ProjectRepositoryUrl, StringComparison.Ordinal),
                    "About silently ignored an external-link launch failure.");
                viewModel.CloseDialog();
                filePicker.OpenUriException = new InvalidOperationException("simulated launcher failure");
                viewModel.OpenUpstreamAsync().GetAwaiter().GetResult();
                Require(viewModel.IsDialogOpen && viewModel.DialogMessage.Contains("https://github.com/Scobalula/Alchemist", StringComparison.Ordinal),
                    "About silently ignored an external-link launch exception.");
                viewModel.CloseDialog();
                filePicker.OpenUriException = null;

                var platformCalls = 0;
                var shellCalls = 0;
                var fallbackUri = new Uri(MainWindowViewModel.ProjectRepositoryUrl);
                var fallbackResult = ExternalUriLauncher.OpenAsync(
                    fallbackUri,
                    _ => { platformCalls++; return Task.FromResult(false); },
                    _ => { shellCalls++; return true; }).GetAwaiter().GetResult();
                Require(fallbackResult && platformCalls == 1 && shellCalls == 1,
                    "External-link launcher did not fall back to Windows ShellExecute.");
                shellCalls = 0;
                var primaryResult = ExternalUriLauncher.OpenAsync(
                    fallbackUri,
                    _ => Task.FromResult(true),
                    _ => { shellCalls++; return true; }).GetAwaiter().GetResult();
                Require(primaryResult && shellCalls == 0,
                    "External-link launcher used the shell fallback after the platform launch succeeded.");
                var exceptionFallbackResult = ExternalUriLauncher.OpenAsync(
                    fallbackUri,
                    _ => throw new InvalidOperationException("simulated platform failure"),
                    _ => true).GetAwaiter().GetResult();
                Require(exceptionFallbackResult,
                    "External-link launcher did not recover from a platform-launch exception.");
                viewModel.Preview.ToggleFirstPerson();
                Require(viewModel.Preview.IsFirstPerson
                    && viewModel.Preview.CameraModeLabel == "Return to orbit view / 1"
                    && viewModel.Preview.FirstPersonBadge == "FIRST PERSON · 90° FOV",
                    "First-person preview state or English accessibility text is invalid.");
                viewModel.Preview.ToggleFirstPerson();
                Require(!viewModel.Preview.IsFirstPerson, "First-person preview did not return to orbit mode.");

                Require(viewModel.AddAnimationPaths([Path.Combine(testDirectory, "idle.cast")]) == 1, "Animation import routing failed.");
                Require(viewModel.SelectedAnimation?.OutputFolder == string.Empty, "New animation output folder must stay blank.");
                var handsPath = Path.Combine(testDirectory, "misleading_weapon_name.cast");
                var weaponPath = Path.Combine(testDirectory, "receiver.cast");
                var attachmentPath = Path.Combine(testDirectory, "magazine.cast");
                WriteClassificationFixture(handsPath,
                [
                    ("tag_origin", -1), ("tag_view", 0), ("j_mainroot", 0),
                    ("j_shoulder_le", 2), ("j_elbow_le", 3), ("j_wrist_le", 4),
                    ("j_shoulder_ri", 2), ("j_elbow_ri", 6), ("j_wrist_ri", 7),
                    ("j_gun", 8), ("tag_weapon", 2),
                ]);
                WriteClassificationFixture(weaponPath,
                [
                    ("j_gun", -1), ("tag_align_gun", 0), ("tag_barrel_attach", 0), ("j_slide", 0),
                ]);
                WriteClassificationFixture(attachmentPath,
                [
                    ("tag_mag_attach", -1), ("j_mag1", 0), ("j_ammo_01", 1),
                ]);
                var handsClassification = ModelPartClassifier.Classify(handsPath);
                var weaponClassification = ModelPartClassifier.Classify(weaponPath);
                var attachmentClassification = ModelPartClassifier.Classify(attachmentPath);
                Require(handsClassification.Kind == ModelPartKind.ViewHands && handsClassification.Confidence >= 0.98f,
                    "A bilateral hand rig containing j_gun was misclassified.");
                Require(weaponClassification.Kind == ModelPartKind.Weapon && weaponClassification.Confidence >= 0.9f
                    && weaponClassification.RecommendedParentBone == "tag_weapon", "Weapon topology classification failed.");
                Require(attachmentClassification.Kind == ModelPartKind.Attachment && attachmentClassification.Confidence >= 0.8f,
                    "Attachment-root classification failed.");
                Require(viewModel.AddPartPaths([handsPath, weaponPath, attachmentPath]) == 3, "Part import routing failed.");
                Require(viewModel.Parts[0].Type == ModelPartKind.ViewHands, "Hands classification was not applied to the workspace.");
                Require(viewModel.Parts[1].Type == ModelPartKind.Weapon && viewModel.Parts[1].ParentBoneTag == "tag_weapon", "Weapon classification or parenting failed.");
                Require(viewModel.Parts[2].Type == ModelPartKind.Attachment && viewModel.Parts[2].ParentBoneTag == string.Empty,
                    "Attachment classification or parenting failed.");
                Require(viewModel.Parts.All(part => part.AutoClassification is not null), "Automatic classification evidence was not retained for review.");
                viewModel.Parts[1].Type = ModelPartKind.Attachment;
                viewModel.SelectedPart = viewModel.Parts[1];
                Require(viewModel.Parts[1].AutoClassification?.Kind == ModelPartKind.Weapon
                    && viewModel.SelectedPartDetection.Contains("Manually overridden", StringComparison.Ordinal),
                    "Manual part-type override hid or replaced the detection recommendation.");
                viewModel.Parts[1].Type = ModelPartKind.Weapon;
                Require(viewModel.AddLayerPaths([Path.Combine(testDirectory, "sprint.cast")]) == 1, "Layer-priority import routing failed.");

                var placements = AnimationTimelineLayout.Calculate([
                    new AnimationTimelineSpan(0, 60),
                    new AnimationTimelineSpan(10, 20),
                    new AnimationTimelineSpan(-5, 10),
                ]);
                Require(placements[0].DurationFrames == 60 && placements[1].DurationFrames == 20,
                    "Timeline bars do not preserve differing animation durations.");
                Require(placements[1].LeadingFrames == 15 && placements[2].LeadingFrames == 0,
                    "Timeline bars do not reflect positive and negative layer offsets.");

                var projectPath = Path.Combine(testDirectory, "roundtrip.aprj");
                projectStore.Save(viewModel.Workspace, projectPath);
                var roundtrip = projectStore.Load(projectPath);
                Require(roundtrip.Animations.Count == 1 && roundtrip.Animations[0].Layers.Count == 1, "AOT project round-trip lost animation layers.");
                Require(roundtrip.Parts.Count == 3 && roundtrip.Parts[1].ParentBoneTag == "tag_weapon", "AOT project round-trip lost model hierarchy settings.");
                Require(roundtrip.Parts.All(part => part.AutoClassification is null), "Transient classification data leaked into the project format.");

                var legacyProjectPath = Path.Combine(testDirectory, "legacy-reference.aprj");
                File.WriteAllText(legacyProjectPath, """
                    {"$id":"1","OutputFormat":".cast","Animations":{"$id":"2","$values":[]},"Parts":{"$id":"3","$values":[]}}
                    """);
                var legacy = projectStore.Load(legacyProjectPath);
                Require(legacy.Animations.Count == 0 && legacy.Parts.Count == 0, "Legacy reference-preserved project compatibility failed.");
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, true);
            }

            Console.WriteLine("Alchemy Stars Avalonia AOT self-test passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    public static int RunHawk(IReadOnlyList<string> arguments)
    {
        try
        {
            Require(arguments.Count == 6, "Hawk smoke requires: hands, weapon, idle, sprint loop, additive offset, output folder.");
            var handsClassification = ModelPartClassifier.Classify(arguments[0]);
            var weaponClassification = ModelPartClassifier.Classify(arguments[1]);
            Require(handsClassification.Kind == ModelPartKind.ViewHands && handsClassification.Confidence >= 0.98f,
                "Hawk hands were not classified as view hands.");
            Require(weaponClassification.Kind == ModelPartKind.Weapon && weaponClassification.Confidence >= 0.9f
                && weaponClassification.RecommendedParentBone == "tag_weapon", "Hawk receiver was not classified as a weapon.");
            var outputFolder = Path.GetFullPath(arguments[5]);
            var request = new AnimationExportRequest(
                [
                    new ModelPartSpec(Path.GetFullPath(arguments[0]), ModelPartKind.ViewHands),
                    new ModelPartSpec(Path.GetFullPath(arguments[1]), ModelPartKind.Weapon, "tag_weapon"),
                ],
                [new AnimationExportJob(
                    Path.GetFullPath(arguments[2]),
                    "sat_vm_ar_hawk_sprint_engine_aot",
                    outputFolder,
                    Framerate: 30,
                    EnableLeftHandIk: true,
                    EnableRightHandIk: false,
                    Layers:
                    [
                        new AnimationLayerSpec(Path.GetFullPath(arguments[3]), AnimationLayerKind.Additive),
                        new AnimationLayerSpec(Path.GetFullPath(arguments[4]), AnimationLayerKind.Additive),
                    ])],
                new AnimationExportOptions(
                    new IkChainSpec("j_shoulder_le", "j_elbow_le", "j_wrist_le", "tag_ik_loc_le"),
                    new IkChainSpec("j_shoulder_ri", "j_elbow_ri", "j_wrist_ri", "tag_ik_loc_ri")));

            var result = new AnimationExportEngine().Export(request);
            Require(result.OutputFiles.Count == 1, "Hawk smoke did not produce exactly one output.");
            var output = Path.GetFullPath(result.OutputFiles[0]);
            Require(File.Exists(output) && new FileInfo(output).Length > 0, "Hawk smoke output is missing or empty.");
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    public static int RunProject(IReadOnlyList<string> arguments)
    {
        try
        {
            Require(arguments.Count == 2, "Project smoke requires: project file and output folder.");
            var store = new WorkspaceProjectStore();
            var document = store.Load(Path.GetFullPath(arguments[0]));
            var outputFolder = Path.GetFullPath(arguments[1]);
            Directory.CreateDirectory(outputFolder);
            foreach (var animation in document.Animations)
                animation.OutputFolder = outputFolder;
            var result = new AnimationExportEngine().Export(store.CreateExportRequest(document));
            Require(result.OutputFiles.Count == document.Animations.Count, "Project smoke output count mismatch.");
            Require(result.OutputFiles.All(path => File.Exists(path) && new FileInfo(path).Length > 0), "Project smoke output is missing or empty.");
            foreach (var output in result.OutputFiles)
                Console.WriteLine(Path.GetFullPath(output));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void WriteClassificationFixture(string path, (string Name, int Parent)[] definitions)
    {
        ulong hash = 1;
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = hash++ };
        var model = new ModelNode { Parent = root, Hash = hash++ };
        var skeleton = new SkeletonNode { Parent = model, Hash = hash++ };
        foreach (var definition in definitions)
        {
            var bone = new BoneNode { Parent = skeleton, Hash = hash++ };
            bone.AddString("n", definition.Name);
            bone.AddValue("p", definition.Parent < 0 ? uint.MaxValue : (uint)definition.Parent);
        }
        CastWriter.Save(path, new Cast.NET.Cast([root]));
    }

    private sealed class SelfTestFilePicker : IWorkspaceFilePicker
    {
        public Uri? LastOpenedUri { get; private set; }
        public bool OpenUriResult { get; set; } = true;
        public Exception? OpenUriException { get; set; }

        public Task<IReadOnlyList<string>> PickFilesAsync(FilePickerPurpose purpose, bool allowMultiple) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> PickProjectDestinationAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string? currentPath) => Task.FromResult<string?>(null);
        public Task<bool> OpenUriAsync(Uri uri)
        {
            LastOpenedUri = uri;
            if (OpenUriException is not null)
                throw OpenUriException;
            return Task.FromResult(OpenUriResult);
        }
    }
}
