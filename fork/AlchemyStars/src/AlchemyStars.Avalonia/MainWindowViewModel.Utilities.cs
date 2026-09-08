namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private long savedArmsSelectionVersion;
    public bool RememberArms
    {
        get => preferences.Snapshot().RememberArms;
        set { preferences.SaveRememberArms(value); RefreshUtilitySettings(); }
    }

    public string SavedArmsStatus
    {
        get
        {
            var path = preferences.Snapshot().SavedArmsPath;
            if (string.IsNullOrWhiteSpace(path)) return Text.NoSavedArms;
            return (File.Exists(path) ? Text.SavedArmsPrefix : Text.MissingArmsPrefix) + path;
        }
    }
    public bool HasSavedArms => !string.IsNullOrWhiteSpace(preferences.Snapshot().SavedArmsPath);

    private void RememberImportedArms(WorkspacePart part)
    {
        if (part.Type != ModelPartKind.ViewHands) return;
        preferences.RememberFirstArms(part.FilePath);
        RefreshUtilitySettings();
    }

    public void ForgetSavedArms()
    {
        savedArmsSelectionVersion++;
        preferences.SaveArmsPath(null);
        RefreshUtilitySettings();
    }

    public async Task ChooseSavedArmsAsync()
    {
        var selection = ++savedArmsSelectionVersion;
        var path = (await picker.PickFilesAsync(FilePickerPurpose.ModelPart, false)).FirstOrDefault();
        if (path is null || selection != savedArmsSelectionVersion) return;
        try
        {
            // Parse the file before retaining it; the picker explicitly identifies this as the user's arms model.
            var classification = await PartClassifier(path);
            if (selection != savedArmsSelectionVersion) return;
            if (classification is null) throw new InvalidDataException(Text.PartDetectionFailed);
            preferences.SaveArmsPath(Path.GetFullPath(path));
            RefreshUtilitySettings();
        }
        catch (Exception error)
        {
            if (selection == savedArmsSelectionVersion) ShowDialog(Text.RememberArmsLabel, error.Message, true);
        }
    }

    private void RefreshUtilitySettings()
    {
        OnPropertyChanged(nameof(RememberArms));
        OnPropertyChanged(nameof(SavedArmsStatus));
        OnPropertyChanged(nameof(HasSavedArms));
    }
}

public sealed partial class UiText
{
    public string Utilities => L("实用功能", "Utilities");
    public string RememberArmsLabel => L("自动复用首次导入的手臂模型", "Reuse the first imported arms model");
    public string RememberArmsHelp => L("默认开启。启动或新建项目时自动加入已记住的手臂；打开已有项目不受影响。关闭不会删除记录或当前模型。", "On by default. Adds the remembered arms at startup or in a new project; existing projects are unchanged. Turning off keeps the saved path and current models.");
    public string NoSavedArms => L("尚未记住手臂；首次导入后自动保存位置。", "No arms saved yet; the first import will be remembered.");
    public string SavedArmsPrefix => L("已记住：", "Remembered: ");
    public string MissingArmsPrefix => L("文件不存在，已跳过复用：", "File missing; reuse skipped: ");
    public string ChooseSavedArms => L("选择手臂模型…", "Choose arms model…");
    public string ForgetSavedArms => L("清除记录", "Forget arms");
}
