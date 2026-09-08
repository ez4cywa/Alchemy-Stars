using System.Diagnostics;

namespace AlchemyStars.Avalonia;

internal static class ExternalUriLauncher
{
    public static Task<bool> OpenAsync(Uri uri, Func<Uri, Task<bool>> platformLaunch) =>
        OpenAsync(uri, platformLaunch, LaunchWithWindowsShell);

    internal static async Task<bool> OpenAsync(
        Uri uri,
        Func<Uri, Task<bool>> platformLaunch,
        Func<Uri, bool> shellLaunch)
    {
        try
        {
            if (await platformLaunch(uri))
                return true;
        }
        catch
        {
            // Some Windows browser associations are not handled by Avalonia's launcher.
            // Fall through to ShellExecute so the About action still has a second route.
        }

        try
        {
            return shellLaunch(uri);
        }
        catch
        {
            return false;
        }
    }

    private static bool LaunchWithWindowsShell(Uri uri)
    {
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
        return true;
    }
}
