namespace AlchemyStars.Avalonia;

public sealed partial class UiText
{
    public string CodDbNavigation => L("COD 武器库", "COD weapon DB");
    public string CodDbTitle => L("COD 武器代号库", "COD weapon codename database");
    public string CodDbHelp => L(
        "离线查询武器名与控制台代号，并可从 Call of Duty Wiki 取回同款图标用于对照。数据来自 CODWeaponDB v0.12.0。",
        "Offline lookup of weapon names and console codenames, with reference icons fetched from the Call of Duty Wiki. Data comes from CODWeaponDB v0.12.0.");

    // Search and filters
    public string CodDbSearch => L("搜索", "Search");
    public string CodDbSearchWatermark => L("武器名、代号或蓝图名", "Weapon, codename or blueprint");
    public string CodDbSearchHelp => L(
        "代号完全匹配优先，其次为前缀、武器名与蓝图名。",
        "An exact codename ranks first, then prefixes, weapon names and blueprint names.");
    public string CodDbGameFilter => L("游戏", "Game");
    public string CodDbClassFilter => L("类别", "Class");
    public string CodDbPrefixFilter => L("代号前缀", "Codename prefix");
    public string CodDbAllGames => L("全部游戏", "All games");
    public string CodDbAllClasses => L("全部类别", "All classes");
    public string CodDbPrefixWatermark => L("例如 sm_ / ar_", "for example sm_ / ar_");
    public string CodDbClearFilters => L("清除筛选", "Clear filters");
    public string CodDbWeapons => L("武器", "Weapons");
    public string CodDbResultsEmpty => L("没有匹配的武器。", "No weapon matches the current search.");
    public string CodDbSelectHint => L("选择左侧武器查看代号、蓝图与对照图标。", "Select a weapon to see its codename, blueprints and reference icon.");
    public string CodDbLoading => L("正在读取武器库…", "Loading the weapon database…");
    public string CodDbLoadFailed => L("武器库读取失败", "Weapon database failed to load");
    public string CodDbUnknownClass => L("未分类", "Unclassified");
    public string CodDbManualRevision => L("人工修订", "Manual revision");

    // Details
    public string CodDbCodename => L("控制台代号", "Console codename");
    public string CodDbNoCodename => L("未记录", "Not recorded");
    public string CodDbCopyCodename => L("复制代号", "Copy codename");
    public string CodDbCopied => L("已复制 {0}", "Copied {0}");
    public string CodDbDetails => L("记录详情", "Record details");
    public string CodDbGame => L("游戏", "Game");
    public string CodDbEngine => L("引擎", "Engine");
    public string CodDbClass => L("类别", "Class");
    public string CodDbSection => L("来源小节", "Source section");
    public string CodDbScope => L("模式", "Mode");
    public string CodDbRevision => L("Wiki 修订", "Wiki revision");
    public string CodDbSourceKind => L("来源", "Source");
    public string CodDbOpenRevision => L("打开来源链接", "Open source link");

    // Blueprints
    public string CodDbBlueprints => L("蓝图", "Blueprints");
    public string CodDbNoBlueprints => L("该武器没有匹配到蓝图记录。", "No blueprint record matched this weapon.");
    public string CodDbBlueprintCodename => L("蓝图代号", "Blueprint codename");
    public string CodDbBlueprintRarity => L("稀有度", "Rarity");
    public string CodDbBlueprintObtain => L("获取方式", "How to obtain");
    public string CodDbBlueprintImage => L("查看蓝图图", "View blueprint image");
    public string CodDbBlueprintSource => L("打开蓝图来源", "Open blueprint source");

    // Wiki reference icon
    public string CodDbReference => L("图标对照", "Icon reference");
    public string CodDbReferenceHelp => L(
        "从 Call of Duty Wiki 取回该武器图标，与当前模型并排对照。首次获取需要联网，之后使用本地缓存。",
        "Fetches this weapon's icon from the Call of Duty Wiki so it can be compared beside your model. The first fetch needs a network connection; later lookups use the local cache.");
    public string CodDbFetchIcon => L("获取图标", "Fetch icon");
    public string CodDbRefreshIcon => L("重新获取", "Refresh");
    public string CodDbFetching => L("正在获取图标…", "Fetching the icon…");
    public string CodDbIconUnavailable => L("还没有图标。", "No icon yet.");
    public string CodDbIconFailed => L("获取图标失败", "Icon lookup failed");
    public string CodDbIconCached => L("本地缓存 · {0}", "Local cache · {0}");
    public string CodDbIconFetched => L("联网获取 · {0}", "Fetched · {0}");
    public string CodDbCopyImage => L("复制图片", "Copy image");
    public string CodDbSaveImage => L("保存图片", "Save image");
    public string CodDbImageSaved => L("已保存到 {0}", "Saved to {0}");
    public string CodDbSaveImageFailed => L("保存图片失败", "Saving the image failed");
    public string CodDbOpenWiki => L("打开 Wiki 页面", "Open wiki page");
    public string CodDbOpenCompare => L("对照窗口", "Comparison window");
    public string CodDbAttributionHelp => L(
        "图标来自 Call of Duty Wiki（CC BY-SA 3.0），仅在本机对照使用，不会写入导出文件。",
        "Icons come from the Call of Duty Wiki (CC BY-SA 3.0). They are used for local reference only and are never written into exports.");

    // Comparison window
    public string CodDbCompareTitle => L("图标对照 · {0}", "Icon reference · {0}");
    public string CodDbCompareTopmost => L("始终置顶", "Always on top");
    public string CodDbCompareScale => L("缩放", "Scale");
    public string CodDbCompareHint => L(
        "把这个窗口放在 CAST 预览旁边，即可用真实图标核对武器比例与轮廓。",
        "Place this window beside the CAST preview to check the weapon's proportions and silhouette against the real icon.");
    public string CodDbCompareEmpty => L("先在武器库页面获取图标。", "Fetch an icon on the weapon database page first.");

    // Dataset management
    public string CodDbDataset => L("数据集", "Dataset");
    public string CodDbDatasetBuiltIn => L("内置快照", "Bundled snapshot");
    public string CodDbDatasetExternal => L("外部目录", "External folder");
    public string CodDbChooseDataset => L("导入数据目录…", "Import data folder…");
    public string CodDbRestoreDataset => L("恢复内置快照", "Restore bundled snapshot");
    public string CodDbDatasetHelp => L(
        "选择 CODWeaponDB 的 dist 目录（需含 weapons.jsonl），即可用更新后的数据替换内置 v0.12.0 快照。",
        "Choose a CODWeaponDB dist folder (it must contain weapons.jsonl) to replace the bundled v0.12.0 snapshot with newer data.");
    public string CodDbDatasetLoaded => L("已加载 {0} 条记录 · {1} 条蓝图", "Loaded {0} records · {1} blueprints");
    public string CodDbDatasetDirHelp => L("当前目录：{0}", "Current folder: {0}");
    public string CodDbDatasetInvalid => L(
        "该目录没有 weapons.jsonl，请选择 CODWeaponDB 的 dist 目录。",
        "That folder has no weapons.jsonl. Choose a CODWeaponDB dist folder.");
    public string CodDbDatasetRestored => L("已恢复内置 v0.12.0 快照。", "Restored the bundled v0.12.0 snapshot.");
    public string CodDbRepository => L("上游数据仓库", "Upstream data repository");
    public string CodDbOpenRepository => L("打开数据仓库", "Open data repository");
    public string CodDbBlueprintsFetched => L("蓝图抓取时间", "Blueprints fetched");
    public string CodDbQaIssues => L("待复核问题", "Open QA issues");
    public string CodDbLicense => L("许可", "License");
}
