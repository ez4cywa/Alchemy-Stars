namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    public async Task ExportBoundModelAsync()
    {
        if (!CanExportBoundModel) return;
        var weapon = Parts.First(part => part.Type == ModelPartKind.Weapon);
        var initialDirectory = HasUnifiedOutputDirectory
            ? UnifiedOutputDirectory
            : SelectedAnimation?.EffectiveOutputFolder;
        if (string.IsNullOrWhiteSpace(initialDirectory))
            initialDirectory = Path.GetDirectoryName(weapon.FilePath);
        var outputDirectory = await picker.PickFolderAsync(initialDirectory);
        if (string.IsNullOrWhiteSpace(outputDirectory)) return;

        try
        {
            IsBusy = true;
            BusyMessage = Text.ExportingModel;
            FooterStatus = Text.ExportingModel;
            var request = new ModelExportRequest(
                Parts.Select(part => new ModelPartSpec(part.FilePath, part.Type, part.ParentBoneTag)).ToArray(),
                outputDirectory,
                AlchemyStars.Engine.OutputFormats.ToExportFormat(Workspace.OutputFormat),
                Workspace.MatchOldCallOfDuty,
                Workspace.OutputUpAxis);
            var result = await Task.Run(() => new ModelExportEngine().Export(request));
            FooterStatus = string.Format(Text.ModelExportComplete, Path.GetFileName(result.OutputFile));
            ShowDialog(Text.ModelExportCompleteTitle,
                string.Format(Text.ModelExportCompleteBody, result.BoneCount, result.MeshCount) + Environment.NewLine + result.OutputFile,
                false);
        }
        catch (Exception exception)
        {
            FooterStatus = Text.ModelExportFailed;
            ShowDialog(Text.ModelExportFailed, LocalizeExportError(exception), true);
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
        }
    }

    private async Task LoadAnimationPreviewSnapshotAsync(AnimationExportRequest request, int previewIndex, string displayPath)
    {
        var cache = Path.Combine(Path.GetTempPath(), "AlchemyStarsPreview", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(cache);
            var source = request.Animations[previewIndex];
            var previewJob = source with { OutputFolder = cache, OutputName = "composition" };
            var previewRequest = request with
            {
                Animations = [previewJob],
                Options = request.Options with
                {
                    Format = ExportFormat.Cast,
                    OutputPrefix = string.Empty,
                    OutputSuffix = string.Empty,
                    CastAnimationOnly = false,
                },
            };
            var result = await Task.Run(() => engine.Export(previewRequest));
            var previewAxis = CastPreviewScene.ResolvePreviewUpAxis(previewRequest.Options.OutputUpAxis, source.SourceFile);
            await Preview.LoadAsync(result.OutputFiles.Single(), displayPath, upAxisOverride: previewAxis);
        }
        finally
        {
            if (Directory.Exists(cache))
                try { Directory.Delete(cache, recursive: true); }
                catch (IOException) { FooterStatus = Text.PreviewCacheRetained; }
                catch (UnauthorizedAccessException) { FooterStatus = Text.PreviewCacheRetained; }
        }
    }
}

public sealed partial class UiText
{
    public string ExportBoundModel => L("导出绑定模型", "Export bound model");
    public string ExportBoundModelHelp => L("按动画混合的骨架挂接和蒙皮规则，导出手臂、武器及附件。", "Export hands, weapon and attachments using the animation merge skeleton and skin binding.");
    public string ExportingModel => L("正在合并并导出绑定模型…", "Merging and exporting the bound model…");
    public string ModelExportComplete => L("已导出绑定模型：{0}", "Exported bound model: {0}");
    public string ModelExportCompleteTitle => L("模型导出成功", "Model export complete");
    public string ModelExportCompleteBody => L("已输出绑定模型，包含 {0} 根骨骼、{1} 个网格：", "Exported a bound model with {0} bones and {1} meshes:");
    public string ModelExportFailed => L("模型导出失败", "Model export failed");
    public string PreviewRefreshFailed => L("动画已导出，但预览刷新失败：", "The animation was exported, but preview refresh failed: ");
}
