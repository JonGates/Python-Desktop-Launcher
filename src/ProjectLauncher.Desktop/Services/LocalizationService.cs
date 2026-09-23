using System.ComponentModel;
using System.Globalization;
using System.Windows;
using ProjectLauncher.Core.Localization;

namespace ProjectLauncher.Desktop.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();
    private LanguagePreferences? _preferences;
    private string _language = "zh-CN";
    public string? PreferenceError { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Language { get => _language; set => SetLanguage(value); }
    public string this[string key] => TextCatalog.Format(Language, key);
    public void Initialize(string path, string? initialLanguage = null)
    {
        _preferences = new(path);
        var stored = _preferences.Load();
        _language = TextCatalog.Normalize(initialLanguage ?? stored.Language, CultureInfo.CurrentUICulture.Name);
        TextCatalog.Language = _language;
        PreferenceError = stored.Error is null ? null : TextCatalog.Format(Language, "Error.PreferenceRead", stored.Error);
        Changed();
    }
    public void SetLanguage(string language)
    {
        if (!Application.Current.Dispatcher.CheckAccess()) { Application.Current.Dispatcher.Invoke(() => SetLanguage(language)); return; }
        if (language is not ("zh-CN" or "en-US") || language == _language) return;
        _language = language; TextCatalog.Language = language;
        var error = _preferences?.Save(language);
        PreferenceError = error is null ? null : TextCatalog.Format(language, "Error.PreferenceSave", error);
        Changed();
    }
    private void Changed()
    {
        PropertyChanged?.Invoke(this, new(nameof(Language)));
        PropertyChanged?.Invoke(this, new("Item[]"));
        PropertyChanged?.Invoke(this, new(nameof(PreferenceError)));
    }
}
