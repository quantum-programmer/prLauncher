using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Serilog;

namespace RDesigner.Resources;

public enum AppLanguage
{
    Russian,
    English
}

public static class LocalizationManager
{
    private const AppLanguage DefaultLanguage = AppLanguage.Russian;
    private const string SettingsDirectoryName = "RDesigner";
    private const string LanguageSettingsFileName = "language.txt";

    private static readonly Dictionary<string, FieldInfo> ActiveStringFields = typeof(AppStrings)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(string))
        .ToDictionary(field => field.Name);

    private static readonly IReadOnlyDictionary<string, string> RussianStrings = ReadStrings(typeof(AppStrings));
    private static readonly IReadOnlyDictionary<string, string> EnglishStrings = ReadStrings(typeof(EnglishAppStrings));

    public static AppLanguage CurrentLanguage { get; private set; } = DefaultLanguage;

    public static event EventHandler? LanguageChanged;

    public static void InitializeResources()
    {
        CurrentLanguage = LoadLanguage();
        Apply(CurrentLanguage);
    }

    public static void ToggleLanguage()
    {
        var language = CurrentLanguage == AppLanguage.Russian
            ? AppLanguage.English
            : AppLanguage.Russian;

        Apply(language);
        SaveLanguage(language);
        Log.Information(AppStrings.LanguageChanged, language);
    }

    public static bool TryToggleLanguage(KeyEventArgs e)
    {
        if (e.Key != Key.U || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return false;

        e.Handled = true;
        ToggleLanguage();
        return true;
    }

    public static void BindWindowTitle(Window window, string key)
    {
        void UpdateTitle(object? sender, EventArgs e)
        {
            if (ActiveStringFields.TryGetValue(key, out var field))
                window.Title = (string?)field.GetValue(null) ?? string.Empty;
        }

        UpdateTitle(null, EventArgs.Empty);
        LanguageChanged += UpdateTitle;
        window.Closed += (_, _) => LanguageChanged -= UpdateTitle;
    }

    private static void Apply(AppLanguage language)
    {
        var strings = language == AppLanguage.Russian ? RussianStrings : EnglishStrings;
        foreach (var pair in strings)
        {
            if (ActiveStringFields.TryGetValue(pair.Key, out var field))
                field.SetValue(null, pair.Value);

            if (Application.Current != null)
                Application.Current.Resources[pair.Key] = pair.Value;
        }

        CurrentLanguage = language;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static IReadOnlyDictionary<string, string> ReadStrings(Type type)
    {
        return type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .ToDictionary(
                field => field.Name,
                field => (string?)field.GetValue(null) ?? string.Empty);
    }

    private static AppLanguage LoadLanguage()
    {
        try
        {
            var value = File.ReadAllText(GetLanguageSettingsPath()).Trim();
            if (Enum.TryParse(value, ignoreCase: true, out AppLanguage language)
                && Enum.IsDefined(language))
            {
                return language;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        SaveLanguage(DefaultLanguage);
        return DefaultLanguage;
    }

    private static void SaveLanguage(AppLanguage language)
    {
        try
        {
            var settingsPath = GetLanguageSettingsPath();
            var settingsDirectory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(settingsDirectory))
                Directory.CreateDirectory(settingsDirectory);

            File.WriteAllText(settingsPath, language.ToString());
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string GetLanguageSettingsPath()
    {
        return Path.Combine(
            GetLocalApplicationDataDirectory(),
            SettingsDirectoryName,
            LanguageSettingsFileName);
    }

    private static string GetLocalApplicationDataDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localApplicationData))
            return localApplicationData;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsWindows()
            ? Path.Combine(userProfile, "AppData", "Local")
            : Path.Combine(userProfile, ".local", "share");
    }
}
