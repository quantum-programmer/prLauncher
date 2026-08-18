using System;
using System.Diagnostics;

namespace Pyramid.Resources;

internal static class ProcessLanguageEnvironment
{
    public static void Apply(ProcessStartInfo startInfo)
    {
        if (startInfo.UseShellExecute)
            return;

        if (OperatingSystem.IsLinux())
        {
            startInfo.Environment.Remove("LC_ALL");
            startInfo.Environment["LANGUAGE"] = GetLanguage();
            startInfo.Environment["LC_MESSAGES"] = GetMessageLocale();
        }
        else if (OperatingSystem.IsWindows())
        {
            // PostgreSQL gettext honors LANGUAGE on Windows without changing
            // the locale used to initialize the database cluster.
            startInfo.Environment["LANGUAGE"] = GetLanguage();
        }
    }

    public static void ApplyToCurrentProcess()
    {
        if (OperatingSystem.IsLinux())
        {
            Environment.SetEnvironmentVariable("LC_ALL", null);
            Environment.SetEnvironmentVariable("LANGUAGE", GetLanguage());
            Environment.SetEnvironmentVariable("LC_MESSAGES", GetMessageLocale());
        }
        else if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable("LANGUAGE", GetLanguage());
        }
    }

    public static string CreateShellPreamble()
    {
        return $"unset LC_ALL\nexport LANGUAGE={GetLanguage()}\nexport LC_MESSAGES={GetMessageLocale()}";
    }

    private static string GetLanguage() =>
        LocalizationManager.CurrentLanguage == AppLanguage.English ? "en" : "ru";

    private static string GetMessageLocale() =>
        LocalizationManager.CurrentLanguage == AppLanguage.English ? "C" : "ru_RU.UTF-8";
}
