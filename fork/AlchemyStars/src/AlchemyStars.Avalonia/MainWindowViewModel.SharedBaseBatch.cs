using System.Globalization;

namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    internal void ReportSharedBaseBatchError(Exception error) => ShowDialog(Text.SharedBaseBatchTitle, error.Message, true);
    public async Task AddSharedBaseBatchAsync()
    {
        if (IsBusy) return;
        var template = SelectedAnimation;
        if (template is null || string.IsNullOrWhiteSpace(template.Name))
        {
            ShowDialog(Text.SharedBaseBatchTitle, Text.SharedBaseBatchNeedsAnimation, true);
            return;
        }
        var workspace = Workspace;
        var replacementLayer = SelectedLayer;
        var paths = await picker.PickFilesAsync(FilePickerPurpose.AnimationLayer, true);
        if (IsBusy || !ReferenceEquals(workspace, Workspace) || !Animations.Contains(template)) return;
        if (replacementLayer is not null && !template.Layers.Contains(replacementLayer)) return;
        AddSharedBaseBatchPaths(template, paths, replacementLayer);
    }

    public int AddSharedBaseBatchPaths(WorkspaceAnimation template, IEnumerable<string> paths,
        WorkspaceLayer? replacementLayer = null)
    {
        if (IsBusy || !Animations.Contains(template)) return 0;
        var tasks = SharedBaseAnimationBatch.Create(template, paths, replacementLayer,
            Animations.Select(animation => string.IsNullOrWhiteSpace(animation.OutputName)
                ? Path.GetFileNameWithoutExtension(animation.Name) : animation.OutputName));
        var index = Animations.IndexOf(template) + 1;
        foreach (var task in tasks) Animations.Insert(index++, task);
        if (tasks.Count > 0)
        {
            SelectedAnimation = tasks[0];
            preferences.RememberDirectory("layer", replacementLayer is null
                ? tasks[0].Layers.Last().Name : tasks[0].Layers[template.Layers.IndexOf(replacementLayer)].Name);
            FooterStatus = string.Format(CultureInfo.CurrentCulture, Text.SharedBaseBatchAdded, tasks.Count);
        }
        return tasks.Count;
    }
}

public sealed partial class UiText
{
    public string BatchActions => L("批量创建动画任务", "Create animation batches");
    public string SharedBaseBatchTitle => L("共用前置动画批量创建", "Batch with shared base animation");
    public string SharedBaseBatchMenu => L("共用前置动画批量叠加…", "Batch overlays with shared base…");
    public string SharedBaseBatchHelp => L("复用当前任务的前置动画和设置，为所选的每个文件创建独立任务。有选中层时替换该层并沿用其设置；否则追加新叠加层。重名自动加序号。原任务保持不变，完成后可使用“导出全部”。", "Reuse this task's base animation and settings to create one task per selected file. Replace the selected layer and retain its settings, or append a layer if none is selected. Duplicate names get a numeric suffix. The original stays unchanged; use Export all when ready.");
    public string SharedBaseBatchNeedsAnimation => L("请先选择一个已设置前置动画的任务，再选择要批量叠加的动画文件。", "Select a task with a base animation first, then choose the overlay files.");
    public string SharedBaseBatchAdded => L("已创建 {0} 个共用前置动画的任务，可使用“导出全部”。", "Created {0} tasks with the shared base animation. Ready for Export all.");
}
