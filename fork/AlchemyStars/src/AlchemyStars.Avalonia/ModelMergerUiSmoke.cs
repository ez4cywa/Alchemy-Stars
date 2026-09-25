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
        VerifyNaming(output);
        var assemblyWindow = new ModelMergerAssemblyWindow(view, null);
        assemblyWindow.Show(window);
        try { await assemblyWindow.VerifyAssemblyWorkflowAsync(ModelMergerServiceSmoke.FindFixtures(), output); }
        finally { await assemblyWindow.WaitForCompletionAsync(); assemblyWindow.Close(); }
        await view.VerifyShutdownAsync(ModelMergerServiceSmoke.FindFixtures(), output);
        ModelMergerServiceSmoke.Require(!view.HasActiveTasks, "Workflow left active tasks.");
        Console.WriteLine("MODEL_MERGER_UI_SMOKE_OK: sidebar, new group, add/replace/remove/root, language/state, settings privacy, native scheduling.");
    }

    private static void VerifyNaming(string output)
    {
        ModelMergerServiceSmoke.Require(ModelMergerNaming.WeaponCode("att_sat_vm_ar_eagle_rec_LOD0") == "eagle", "Weapon code derivation failed.");
        ModelMergerServiceSmoke.Require(ModelMergerNaming.WeaponCode("sat_vm_ar_hawk_barl_v3") == "hawk", "Weapon code version stripping failed.");
        ModelMergerServiceSmoke.Require(ModelMergerNaming.WeaponCode("plain_name") == "plain_name", "Weapon code fallback failed.");
        ModelMergerServiceSmoke.Require(ModelMergerNaming.MergeOutputName(output, ["att_sat_vm_ar_eagle_rec_LOD0.cast", "att_sat_vm_ar_eagle_mag_LOD0.cast"]) == "eagle.cast", "Free merge name should use the plain weapon code.");
        File.WriteAllText(Path.Combine(output, "eagle.cast"), string.Empty);
        ModelMergerServiceSmoke.Require(ModelMergerNaming.MergeOutputName(output, ["att_sat_vm_ar_eagle_rec_LOD0.cast", "att_sat_vm_ar_eagle_mag_LOD0.cast"]) == "rec_mag_eagle.cast", "Conflicting merge name should prefix the differing part segments.");
        File.WriteAllText(Path.Combine(output, "rec_mag_eagle.cast"), string.Empty);
        ModelMergerServiceSmoke.Require(ModelMergerNaming.MergeOutputName(output, ["att_sat_vm_ar_eagle_rec_LOD0.cast", "att_sat_vm_ar_eagle_mag_LOD0.cast"]) == "rec_mag_eagle_2.cast", "Second conflict should climb the numeric ladder.");
        File.WriteAllText(Path.Combine(output, "hawk_filled.cast"), string.Empty);
        ModelMergerServiceSmoke.Require(ModelMergerNaming.FillOutputName(output, "D:\\models\\sat_vm_ar_hawk_rec_LOD0.cast") == "hawk_filled_2.cast", "Conflicting fill name should climb the numeric ladder.");
        ModelMergerServiceSmoke.Require(ModelMergerNaming.AssemblyOutputName(output, "D:\\models\\sat_vm_ar_hawk_rec_LOD0.cast") == "hawk_viewhands.cast", "Assembly name should use the viewhands suffix.");
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
