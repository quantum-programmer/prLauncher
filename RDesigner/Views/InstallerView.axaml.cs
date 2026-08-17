using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Pyramid.Resources;
using Pyramid.ViewModels;
using System;
using System.Linq;

namespace Pyramid.Views;

public partial class InstallerView : UserControl
{
    private bool isUpdatingLanguageSelector;

    public InstallerView(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        UpdateLanguageSelector();
        PopulateLicenseAgreement();
        AttachedToVisualTree += (_, _) =>
        {
            LocalizationManager.LanguageChanged -= OnLanguageChanged;
            LocalizationManager.LanguageChanged += OnLanguageChanged;
            UpdateLanguageSelector();
            PopulateLicenseAgreement();
        };
        DetachedFromVisualTree += (_, _) => LocalizationManager.LanguageChanged -= OnLanguageChanged;
        DataContext = serviceProvider.GetRequiredService<InstallerViewModel>();
    }

    private void PopulateLicenseAgreement()
    {
        LicenseDocumentPanel.Children.Clear();

        var licenseText = LocalizationManager.CurrentLanguage == AppLanguage.English
            ? LicenseAgreementText.OfficialEnglish
            : LicenseAgreementText.OfficialRussian;

        var paragraphs = licenseText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (paragraphs.Length == 0)
            return;

        AddTitle(paragraphs[0]);

        foreach (var paragraph in paragraphs.Skip(1))
        {
            if (IsHeading(paragraph))
            {
                AddHeading(paragraph);
            }
            else if (IsSignature(paragraph))
            {
                AddSignature(paragraph);
            }
            else
            {
                AddBodyParagraph(paragraph);
            }
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        UpdateLanguageSelector();
        PopulateLicenseAgreement();
    }

    private void OnLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (isUpdatingLanguageSelector
            || LanguageSelector.SelectedItem is not ComboBoxItem item
            || item.Tag is not string languageName
            || !Enum.TryParse(languageName, ignoreCase: true, out AppLanguage language))
        {
            return;
        }

        LocalizationManager.SelectLanguage(language);
    }

    private void UpdateLanguageSelector()
    {
        isUpdatingLanguageSelector = true;
        try
        {
            LanguageSelector.SelectedIndex = LocalizationManager.CurrentLanguage == AppLanguage.Russian ? 0 : 1;
        }
        finally
        {
            isUpdatingLanguageSelector = false;
        }
    }

    private void AddTitle(string title)
    {
        var lines = title.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < lines.Length; index++)
        {
            LicenseDocumentPanel.Children.Add(new TextBlock
            {
                Text = lines[index],
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Arial"),
                FontSize = index == 0 ? 18 : 16,
                FontWeight = FontWeight.Bold,
                LineHeight = 21,
                Margin = index == lines.Length - 1
                    ? new Thickness(0, 0, 0, 16)
                    : default
            });
        }
    }

    private void AddHeading(string heading)
    {
        LicenseDocumentPanel.Children.Add(new TextBlock
        {
            Text = heading,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Arial"),
            FontSize = 14,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 12, 0, 6)
        });
    }

    private void AddBodyParagraph(string paragraph)
    {
        LicenseDocumentPanel.Children.Add(new TextBlock
        {
            Text = paragraph,
            TextAlignment = TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Arial"),
            FontSize = 14,
            LineHeight = 20,
            Foreground = new SolidColorBrush(Color.Parse("#303030")),
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private void AddSignature(string text)
    {
        LicenseDocumentPanel.Children.Add(new TextBlock
        {
            Text = text,
            Width = 390,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Arial"),
            FontSize = 14,
            Foreground = Brushes.Black,
            Margin = text.StartsWith("Начальник", StringComparison.Ordinal)
                || text.StartsWith("Head of", StringComparison.Ordinal)
                ? new Thickness(0, 28, 0, 18)
                : new Thickness(0, 0, 0, 12)
        });
    }

    private static bool IsHeading(string paragraph)
    {
        return paragraph == "ВАЖНО — ПРОЧТИТЕ ВНИМАТЕЛЬНО!"
            || paragraph == "ЛИЦЕНЗИЯ НА ПРОГРАММУ"
            || paragraph == "IMPORTANT - READ CAREFULLY!"
            || paragraph == "SOFTWARE LICENSE"
            || (paragraph.Length > 2 && char.IsDigit(paragraph[0]) && paragraph[1] == '.');
    }

    private static bool IsSignature(string paragraph)
    {
        return paragraph.StartsWith("Начальник", StringComparison.Ordinal)
            || paragraph.StartsWith("Head of", StringComparison.Ordinal)
            || paragraph.StartsWith("________________", StringComparison.Ordinal)
            || paragraph == "М.П."
            || paragraph == "Seal";
    }
}
