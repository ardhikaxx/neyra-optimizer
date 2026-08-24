using System.Globalization;
using System.Resources;

namespace NeyraOptimizer.App.Localization;

/// <summary>
/// Runtime localization over embedded .resx resources (en neutral, id satellite).
/// XAML binds to the indexer; raising PropertyChanged("Item[]") refreshes every bound string
/// when the language changes at runtime.
/// </summary>
public sealed class Translator : System.ComponentModel.INotifyPropertyChanged
{
    private static Translator? _instance;
    private ResourceManager _resources = null!;
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public static Translator Instance => _instance ?? throw new InvalidOperationException("Translator.Initialize() was not called.");

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public static void Initialize()
    {
        _instance ??= new Translator();
        Instance.SetLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("id", StringComparison.OrdinalIgnoreCase)
            ? "id"
            : "en");
    }

    private Translator()
    {
        _resources = new ResourceManager("NeyraOptimizer.App.Localization.Strings", typeof(Translator).Assembly);
    }

    public CultureInfo Culture => _culture;

    /// <summary>Current language as BCP-47 shorthand ("en"/"id").</summary>
    public string CurrentLanguage => _culture.TwoLetterISOLanguageName;

    public void SetLanguage(string twoLetterCode)
    {
        var culture = twoLetterCode.Equals("id", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("id")
            : new CultureInfo("en");
        if (_culture.Name == culture.Name) return;
        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        // "Item[]" refreshes ALL indexer bindings across the app.
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
    }

    public string this[string key] =>
        _resources.GetString(key, _culture) ?? _resources.GetString(key, CultureInfo.InvariantCulture) ?? $"[{key}]";
}
