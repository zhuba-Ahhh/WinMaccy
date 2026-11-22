namespace WinMaccy.Services;

/// <summary>Resolves "System" dark/light from the registry value Windows personalization writes.</summary>
public static class ThemeService
{
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // AppsUseLightTheme: 1 = light, 0 = dark. Missing/older systems default to light.
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
