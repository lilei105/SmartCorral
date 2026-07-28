using System.Windows;
using SmartCorral;
using SmartCorral.Models;
using SmartCorral.Services;

namespace SmartCorral.Views;

/// <summary>Edits settings (AI provider + icons-per-row). Saved to data/settings.json.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        VersionText.Text = "v" + AppInfo.Version;
        BaseUrlBox.Text = settings.AiBaseUrl;
        KeyBox.Text = settings.AiApiKey;
        ModelBox.Text = settings.AiModel;
        int idx = settings.IconsPerRow - 2; // items are 2..8
        IconsPerRowBox.SelectedIndex = (idx >= 0 && idx < IconsPerRowBox.Items.Count) ? idx : 1; // default 3
        SeparateFoldersBox.IsChecked = settings.SeparateFolders;
        ForceTopmostBox.IsChecked = settings.ForceTopmost;
        UIScaleSlider.Value = Math.Clamp(settings.UIScale, 0.8, 1.3);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.AiBaseUrl = BaseUrlBox.Text.Trim();
        _settings.AiApiKey = KeyBox.Text;
        _settings.AiModel = ModelBox.Text.Trim();
        _settings.IconsPerRow = (IconsPerRowBox.SelectedIndex >= 0 ? IconsPerRowBox.SelectedIndex + 2 : 3);
        _settings.SeparateFolders = SeparateFoldersBox.IsChecked == true;
        _settings.ForceTopmost = ForceTopmostBox.IsChecked == true;
        _settings.UIScale = UIScaleSlider.Value;
        PersistenceService.SaveSettings(_settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void UIScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (UIScaleValue != null && UIScaleSlider != null)
            UIScaleValue.Text = $"{(int)Math.Round(UIScaleSlider.Value * 100)}%";
    }
}
