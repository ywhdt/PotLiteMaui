using PotLiteMaui.Models;

namespace PotLiteMaui.Services;

public interface ITranslationProvider
{
	TranslationProviderDescriptor Descriptor { get; }
	Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}

public interface ITranslationService
{
	IReadOnlyList<TranslationProviderDescriptor> Providers { get; }
	Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
