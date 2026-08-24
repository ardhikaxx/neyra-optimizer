using NeyraOptimizer.Domain.Enums;
using NeyraOptimizer.Domain.Models.System;
using NeyraOptimizer.Domain.Rules;
using NeyraOptimizer.Domain.Settings;

namespace NeyraOptimizer.Application.Session;

/// <summary>
/// Process-wide session state shared by view models. Holds conservative defaults until the user
/// completes onboarding and consents to changes.
/// </summary>
public sealed class SessionState
{
    private readonly object _gate = new();
    private AnalysisBundle? _lastAnalysis;
    private IReadOnlyList<Recommendation> _lastRecommendations = Array.Empty<Recommendation>();

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Set at startup from the Compatibility Checker.</summary>
    public Diagnostics.Compatibility.CompatibilityResult Compatibility { get; set; } =
        new() { IsSupported = true, OsSummary = "Not checked yet" };

    /// <summary>True when system modification must be refused (unsupported OS or user choice).</summary>
    public bool IsReadOnlyMode { get; set; }

    public bool CanModifySystem => !IsReadOnlyMode && Compatibility.IsSupported && Settings.UserConsentedToChanges;

    public UsageProfileKind UsageProfile
    {
        get => Settings.PreferredUsageProfile;
        set => Settings.PreferredUsageProfile = value;
    }

    public AnalysisBundle? LastAnalysis
    {
        get { lock (_gate) return _lastAnalysis; }
        set { lock (_gate) _lastAnalysis = value; }
    }

    public IReadOnlyList<Recommendation> LastRecommendations
    {
        get { lock (_gate) return _lastRecommendations; }
        set { lock (_gate) _lastRecommendations = value; }
    }

    public void LoadSettings(AppSettings settings)
    {
        Settings = settings ?? new AppSettings();
        if (!Settings.UserConsentedToChanges)
            IsReadOnlyMode = true;
    }
}

/// <summary>
/// Guarantees only one mutation batch touches Windows at a time. UI surfaces IsBusy so pages can
/// disable conflicting actions while a batch runs.
/// </summary>
public sealed class OperationLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsBusy { get; private set; }
    public string CurrentOperation { get; private set; } = string.Empty;

    public event EventHandler? Changed;

    public async Task<IDisposable> AcquireAsync(string description, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        IsBusy = true;
        CurrentOperation = description;
        Changed?.Invoke(this, EventArgs.Empty);
        return new Releaser(this);
    }

    private sealed class Releaser : IDisposable
    {
        private OperationLock? _owner;
        public Releaser(OperationLock owner) => _owner = owner;
        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            owner.IsBusy = false;
            owner.CurrentOperation = string.Empty;
            owner.Changed?.Invoke(owner, EventArgs.Empty);
            owner._semaphore.Release();
        }
    }
}
