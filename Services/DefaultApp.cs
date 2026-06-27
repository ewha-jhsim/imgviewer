using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ImgViewer.Services;

/// <summary>
/// Registers ImgViewer as a handler for image files so it shows up in Windows'
/// "Open with" list and "Default apps" settings. Note: since Windows 10, an app may
/// not silently make itself the default for an extension — the user must confirm the
/// choice in Settings. <see cref="OpenDefaultAppsSettings"/> opens that page for them.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DefaultApp
{
    private const string ProgId = "ImgViewer.Image";
    private const string AppName = "ImgViewer";
    private const string AppDescription = "Lightweight image viewer";
    private const string CapabilitiesPath = @"Software\ImgViewer\Capabilities";

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// Creates the ProgID, file associations and capability entries. Writes under
    /// HKCU (per-user, no elevation required) when <paramref name="machineWide"/> is false.
    /// </summary>
    public static bool Register(bool machineWide)
    {
        try
        {
            using RegistryKey classes = OpenClasses(machineWide, writable: true);
            string exe = ExecutablePath();

            // 1) ProgID describing how to open our files.
            using (RegistryKey prog = classes.CreateSubKey(ProgId))
            {
                prog.SetValue(string.Empty, "Image File");
                prog.SetValue("FriendlyTypeName", "Image File");
                using (RegistryKey icon = prog.CreateSubKey("DefaultIcon"))
                    icon.SetValue(string.Empty, $"\"{exe}\",0");
                using RegistryKey cmd = prog.CreateSubKey(@"shell\open\command");
                cmd.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
            }

            // 2) Per-extension association + capability list for the Settings UI.
            RegistryKey root = machineWide ? Registry.LocalMachine : Registry.CurrentUser;
            using (RegistryKey caps = root.CreateSubKey(CapabilitiesPath))
            {
                caps.SetValue("ApplicationName", AppName);
                caps.SetValue("ApplicationDescription", AppDescription);
                using RegistryKey assoc = caps.CreateSubKey("FileAssociations");
                foreach (string ext in ImageLoader.SupportedExtensions)
                {
                    // Register the ProgID as an "open with" candidate for this extension.
                    using (RegistryKey extKey = classes.CreateSubKey(ext))
                    using (RegistryKey owp = extKey.CreateSubKey("OpenWithProgids"))
                        owp.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);

                    assoc.SetValue(ext, ProgId);
                }
            }

            // 3) Advertise the app to the "Default apps" UI.
            using (RegistryKey reg = root.CreateSubKey(@"Software\RegisteredApplications"))
                reg.SetValue(AppName, CapabilitiesPath);

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DefaultApp.Register failed: {ex}");
            return false;
        }
    }

    public static bool Unregister(bool machineWide)
    {
        try
        {
            RegistryKey root = machineWide ? Registry.LocalMachine : Registry.CurrentUser;
            using (RegistryKey classes = OpenClasses(machineWide, writable: true))
            {
                classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
                foreach (string ext in ImageLoader.SupportedExtensions)
                {
                    using RegistryKey? owp = classes.OpenSubKey($@"{ext}\OpenWithProgids", writable: true);
                    owp?.DeleteValue(ProgId, throwOnMissingValue: false);
                }
            }

            using (RegistryKey? reg = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
                reg?.DeleteValue(AppName, throwOnMissingValue: false);
            root.DeleteSubKeyTree(@"Software\ImgViewer", throwOnMissingSubKey: false);

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"DefaultApp.Unregister failed: {ex}");
            return false;
        }
    }

    /// <summary>Returns true if our capability registration is present for this user.</summary>
    public static bool IsRegistered()
    {
        try
        {
            using RegistryKey? reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications");
            return reg?.GetValue(AppName) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Opens the Windows "Default apps" settings page so the user can confirm.</summary>
    public static void OpenDefaultAppsSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"OpenDefaultAppsSettings failed: {ex}");
        }
    }

    private static RegistryKey OpenClasses(bool machineWide, bool writable)
    {
        RegistryKey root = machineWide ? Registry.LocalMachine : Registry.CurrentUser;
        return root.CreateSubKey(@"Software\Classes", writable);
    }

    private static string ExecutablePath()
    {
        // Process.MainModule gives the real .exe path even for single-file apps.
        string? path = Process.GetCurrentProcess().MainModule?.FileName;
        return string.IsNullOrEmpty(path) ? Environment.ProcessPath ?? "ImgViewer.exe" : path;
    }
}
