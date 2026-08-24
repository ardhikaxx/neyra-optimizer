using System.Windows.Data;
using System.Windows.Markup;

namespace NeyraOptimizer.App.Localization;

/// <summary>XAML usage: Text="{loc:Loc Key=Dashboard.Title}" — live-updates on language change.</summary>
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrWhiteSpace(Key))
            return string.Empty;

        return new Binding($"[{Key}]")
        {
            Source = Translator.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };
    }
}
