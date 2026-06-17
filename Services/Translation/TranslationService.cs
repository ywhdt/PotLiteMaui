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

	public async Task<TranslationBatchResult> TranslateManyAsync(
		TranslationBatchRequest request,
		CancellationToken cancellationToken = default)
	{
		var providerIds = request.ProviderIds
			.Where(id => !string.IsNullOrWhiteSpace(id))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (providerIds.Length == 0)
		{
			throw new InvalidOperationException("请至少选择一个翻译服务");
		}

		var tasks = providerIds
			.Select((providerId, order) => TranslateOneAsync(request, providerId, order, cancellationToken))
			.ToArray();
		var items = await Task.WhenAll(tasks);
		var results = items
			.Select(item => item.Result)
			.Where(result => result is not null)
			.Cast<TranslationResult>()
			.ToList();
		var failures = items
			.Select(item => item.Failure)
			.Where(failure => failure is not null)
			.Cast<TranslationProviderFailure>()
			.ToList();

		return new TranslationBatchResult
		{
			SourceText = request.Text,
			SourceLanguage = request.SourceLanguage,
			TargetLanguage = request.TargetLanguage,
			Results = results,
			Failures = failures
		};
	}

	private async Task<(TranslationResult? Result, TranslationProviderFailure? Failure)> TranslateOneAsync(
		TranslationBatchRequest request,
		string providerId,
		int order,
		CancellationToken cancellationToken)
	{
		if (!_providers.TryGetValue(providerId, out var provider))
		{
			return (null, new TranslationProviderFailure
			{
				ProviderId = providerId,
				ProviderName = providerId,
				Order = order,
				ErrorMessage = "未找到该翻译服务"
			});
		}

		try
		{
			var result = await provider.TranslateAsync(new TranslationRequest(
				request.Text,
				request.SourceLanguage,
				request.TargetLanguage,
				providerId,
				request.Settings), cancellationToken);
			result.Order = order;
			return (result, null);
		}
		catch (Exception ex)
		{
			return (null, new TranslationProviderFailure
			{
				ProviderId = provider.Descriptor.Id,
				ProviderName = provider.Descriptor.DisplayName,
				Order = order,
				ErrorMessage = ex.Message
			});
		}
	}
}
