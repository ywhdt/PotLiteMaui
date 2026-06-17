using PotLiteMaui.Models;

namespace PotLiteMaui.Services.Translation;

public sealed class TranslationService(IEnumerable<ITranslationProvider> providers) : ITranslationService
{
	private readonly IReadOnlyDictionary<string, ITranslationProvider> _providers =
		providers.ToDictionary(provider => provider.Descriptor.Id, StringComparer.OrdinalIgnoreCase);

	public IReadOnlyList<TranslationProviderDescriptor> Providers => _providers.Values
		.Select(provider => provider.Descriptor)
		.OrderBy(provider => provider.DisplayName)
		.ToArray();

	public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
	{
		if (!_providers.TryGetValue(request.ProviderId, out var provider))
		{
			throw new InvalidOperationException("未找到选中的翻译引擎");
		}

		return provider.TranslateAsync(request, cancellationToken);
	}
}
