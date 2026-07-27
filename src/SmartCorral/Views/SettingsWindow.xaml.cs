using System.Windows;
using SmartCorral.Models;
using SmartCorral.Services;

namespace SmartCorral.Views;

/// <summary>Edits AI provider settings (baseUrl / apiKey / model). Saved to data/settings.json.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        BaseUrlBox.Text = settings.AiBaseUrl;
        KeyBox.Text = settings.AiApiKey;
        ModelBox.Text = settings.AiModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.AiBaseUrl = BaseUrlBox.Text.Trim();
        _settings.AiApiKey = KeyBox.Text;
        _settings.AiModel = ModelBox.Text.Trim();
        PersistenceService.SaveSettings(_settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
