using CommunityToolkit.Mvvm.ComponentModel;
using NeyraOptimizer.App.Localization;
using NeyraOptimizer.Application.Session;

namespace NeyraOptimizer.App.ViewModels;

/// <summary>Base for all page view models. Provides session access and a localized helper.</summary>
public abstract partial class ViewModelBase : ObservableObject
{
    protected SessionState Session { get; }

    protected ViewModelBase(SessionState session) => Session = session;

    /// <summary>T(key) — localized string; re-evaluated on binding refresh.</summary>
    public string T(string key) => Translator.Instance[key];

    public bool CanModify => Session.CanModifySystem;
    public bool IsReadOnly => !Session.CanModifySystem;
}
