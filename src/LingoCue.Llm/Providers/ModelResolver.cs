using LingoCue.Core.Settings;

namespace LingoCue.Llm.Providers;

/// <summary>
/// Turns the settings (explicit model ids or "automatic") into concrete model ids using the provider's
/// model list, cached for the session.
/// </summary>
public sealed class ModelResolver(ILlmProvider provider)
{
    private IReadOnlyList<LlmModelInfo>? _models;

    public ILlmProvider Provider => provider;

    public async Task<IReadOnlyList<LlmModelInfo>> ModelsAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (_models is null || refresh)
        {
            _models = await provider.ListModelsAsync(cancellationToken);
        }

        return _models;
    }

    public async Task<string> ResolveAsync(LlmSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var explicitId = settings.UseQualityModel ? settings.QualityModel : settings.FastModel;
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId;
        }

        var models = await ModelsAsync(cancellationToken: cancellationToken);
        var picked = settings.UseQualityModel ? ModelCatalog.PickQuality(models) : ModelCatalog.PickFast(models);
        return picked?.Id ?? throw new LlmException("No suitable model found in the provider's model list.")
        {
            Remedy = "Ayarlar → LLM bölümünden bir model seçin.",
        };
    }
}
