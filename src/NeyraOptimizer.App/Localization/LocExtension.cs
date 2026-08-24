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

        // IMPORTANT: delegate to Binding.ProvideValue so WPF attaches a proper
        // BindingExpression. Returning a raw Binding from a custom extension makes the
        // XAML writer throw "'Binding' is not a valid value for property ..." at load.
        var binding = new Binding($"[{Key}]")
        {
            Source = Translator.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
