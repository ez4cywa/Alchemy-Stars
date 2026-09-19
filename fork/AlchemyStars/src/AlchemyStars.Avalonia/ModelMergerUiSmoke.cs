using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class ModelMergerUiSmoke
{
    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var navigation = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ModelMergerNavigation");
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(navigation)!).Invoke();
        await Task.Delay(80);
        ModelMergerServiceSmoke.Require(vm.SelectedPage == WorkspacePage.ModelMerger, "ModelMerger sidebar did not navigate.");
        var view = window.GetVisualDescendants().OfType<ModelMergerView>().Single();
        ModelMergerServiceSmoke.Require(view.IsEffectivelyVisible, "ModelMerger page is hidden.");
        var previousCount = view.GroupCount;
        var newButton = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == view.Text["new"]);
        ((IInvokeProvider)ControlAutomationPeer.CreatePeerForElement(newButton)!).Invoke();
        ModelMergerServiceSmoke.Require(view.GroupCount == previousCount + 1, "New group button did not add a group.");
        var output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Program.RenderSmokePath!))!, "model-merger-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        view.VerifyEditingWorkflow(ModelMergerServiceSmoke.FindFixtures(), output);
        await ModelMergerServiceSmoke.RunAsync(output);
        var ammunitionFixture = FindAmmunitionFixture();
        var ammunitionWindow = new ModelMergerAmmoWindow(view, null);
        ammunitionWindow.Show(window);
        try { await ammunitionWindow.VerifyWorkflowAsync(ammunitionFixture, output); }
        finally { await ammunitionWindow.WaitForCompletionAsync(); ammunitionWindow.Close(); }
        await view.VerifyShutdownAsync(ModelMergerServiceSmoke.FindFixtures(), output);
        ModelMergerServiceSmoke.Require(!view.HasActiveTasks, "Workflow left active tasks.");
        Console.WriteLine("MODEL_MERGER_UI_SMOKE_OK: sidebar, new group, add/replace/remove/root, language/state, settings privacy, native scheduling.");
    }

    private static string FindAmmunitionFixture()
    {
        if (Environment.GetEnvironmentVariable("ALCHEMY_MODEL_MERGER_AMMO_FIXTURES") is { Length: > 0 } configured)
            return Path.GetFullPath(configured);
        for (var directory = new DirectoryInfo(ModelMergerServiceSmoke.FindFixtures()); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "output", "model-merger-ammo-fixture");
            if (File.Exists(Path.Combine(path, "weapon.cast"))) return path;
        }
        throw new DirectoryNotFoundException("Generate the ammunition fixtures with the alchemy-model-merger generate_ammunition_fixture example first.");
    }
}
