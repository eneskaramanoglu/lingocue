using LingoCue.Core.Settings;

namespace LingoCue.Llm.Providers;

/// <summary>
/// Picks the provider named in the settings (Gemini in the cloud or a local Ollama) and turns the
/// settings (explicit model ids or "automatic") into concrete model ids using that provider's model
/// list, cached per provider for the session.
/// </summary>
public sealed class ModelResolver
{
    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly Func<LlmSettings> _settings;
    private readonly Dictionary<string, IReadOnlyList<LlmModelInfo>> _models = new(StringComparer.Ordinal);

    public ModelResolver(IEnumerable<ILlmProvider> providers, Func<LlmSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = [.. providers];
        _settings = settings;
        if (_providers.Count == 0)
        {
            throw new ArgumentException("At least one provider is needed.", nameof(providers));
        }
    }

    /// <summary>One provider, settings defaults (tests and simple setups).</summary>
    public ModelResolver(ILlmProvider provider)
        : this([provider], () => new LlmSettings { Provider = provider.Id })
    {
    }

    public IReadOnlyList<ILlmProvider> Providers => _providers;

    /// <summary>The provider chosen in the settings.</summary>
    public ILlmProvider Provider => ProviderFor(_settings().Provider);

    public ILlmProvider ProviderFor(string? id)
        => _providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? _providers[0];

    public Task<IReadOnlyList<LlmModelInfo>> ModelsAsync(bool refresh = false, CancellationToken cancellationToken = default)
        => ModelsAsync(Provider.Id, refresh, cancellationToken);

    public async Task<IReadOnlyList<LlmModelInfo>> ModelsAsync(string providerId, bool refresh = false, CancellationToken cancellationToken = default)
    {
        var provider = ProviderFor(providerId);
        if (refresh || !_models.TryGetValue(provider.Id, out var models))
        {
            models = await provider.ListModelsAsync(cancellationToken);
            _models[provider.Id] = models;
        }

        return models;
    }

    public async Task<string> ResolveAsync(LlmSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var provider = ProviderFor(settings.Provider);
        if (provider.Id == OllamaProvider.ProviderId)
        {
            return await ResolveLocalAsync(settings, provider, cancellationToken);
        }

        var explicitId = settings.UseQualityModel ? settings.QualityModel : settings.FastModel;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId;
        }

        var models = await ModelsAsync(provider.Id, cancellationToken: cancellationToken);
        var picked = settings.UseQualityModel ? ModelCatalog.PickQuality(models) : ModelCatalog.PickFast(models);
        return picked?.Id ?? throw new LlmException("No suitable model found in the provider's model list.")
        {
            Remedy = "Ayarlar → LLM bölümünden bir model seçin.",
        };
    }

    /// <summary>The chosen local model, or the only one installed; several installed ones need a choice.</summary>
    private async Task<string> ResolveLocalAsync(LlmSettings settings, ILlmProvider provider, CancellationToken cancellationToken)
    {
        var models = await ModelsAsync(provider.Id, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.LocalModel))
        {
            return models.Any(m => string.Equals(m.Id, settings.LocalModel, StringComparison.OrdinalIgnoreCase))
                ? settings.LocalModel
                : throw new LlmException($"Local model {settings.LocalModel} is not installed.")
                {
                    Remedy = $"\"{settings.LocalModel}\" Ollama'da yok. Terminalde indirin: ollama pull {settings.LocalModel} ya da Tercihler'den başka bir yerel model seçin.",
                };
        }

        return models.Count switch
        {
            1 => models[0].Id,
            0 => throw new LlmException("No local model installed.")
            {
                Remedy = "Ollama'da indirilmiş model yok. Terminalde bir model indirin (ör. ollama pull gemma3:12b) ve Tercihler'den seçin.",
            },
            _ => throw new LlmException("Several local models; none chosen.")
            {
                Remedy = "Tercihler → Dil modeli bölümünden hangi yerel modelin kullanılacağını seçin.",
            },
        };
    }
}
