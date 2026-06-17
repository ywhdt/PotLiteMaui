using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PotLiteMaui.Models;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Services.Translation;

public sealed class AzureDictionaryProvider(
	HttpClient httpClient,
	ISecureCredentialStore credentialStore) : ITranslationProvider
{
	public TranslationProviderDescriptor Descriptor { get; } = new(
		TranslationProviderIds.AzureDictionary,
		"Bing 词典",
		IsDictionary: true,
		IsExperimental: false);

	public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
	{
		if (request.SourceLanguage == "auto")
		{
			throw new InvalidOperationException("Bing 词典需要手动选择源语言");
		}

		if (LooksLikeLongText(request.Text))
		{
			throw new InvalidOperationException("Bing 词典适合单词或短语，请切换到 Google 或 OpenAI 翻译长句");
		}

		var key = await credentialStore.GetAsync(SecureCredentialKeys.AzureTranslatorKey);
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new InvalidOperationException("请先填写 Azure Translator Key");
		}

		var endpoint = NormalizeEndpoint(request.Settings.AzureEndpoint);
		var from = LanguageCodeMapper.ToAzure(request.SourceLanguage);
		var to = LanguageCodeMapper.ToAzure(request.TargetLanguage);
		var lookupUri = $"{endpoint}/dictionary/lookup?api-version=3.0&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
		var lookup = await PostAzureAsync<AzureLookupResponse[]>(
			lookupUri,
			request.Settings.AzureRegion,
			key,
			new[] { new AzureDictionaryLookupRequest(request.Text) },
			cancellationToken);

		var first = lookup.FirstOrDefault();
		if (first?.Translations is null || first.Translations.Count == 0)
		{
			throw new InvalidOperationException("Bing 词典没有返回释义");
		}

		var examples = await TryLoadExamplesAsync(endpoint, request, first.Translations[0].NormalizedTarget, from, to, key, cancellationToken);
		var dictionary = new DictionaryResult
		{
			Term = first.DisplaySource ?? request.Text,
			Translations = first.Translations
				.Select(item => new DictionaryTranslation
				{
					DisplayTarget = item.DisplayTarget ?? item.NormalizedTarget ?? string.Empty,
					PartOfSpeech = item.PosTag ?? string.Empty,
					Confidence = item.Confidence,
					BackTranslations = item.BackTranslations?
						.Select(back => back.DisplayText ?? back.NormalizedText ?? string.Empty)
						.Where(value => !string.IsNullOrWhiteSpace(value))
						.ToList() ?? []
				})
				.Where(item => !string.IsNullOrWhiteSpace(item.DisplayTarget))
				.ToList(),
			Examples = examples
		};

		return new TranslationResult
		{
			Kind = TranslationResultKind.Dictionary,
			ProviderId = Descriptor.Id,
			ProviderName = Descriptor.DisplayName,
			SourceText = request.Text,
			SourceLanguage = request.SourceLanguage,
			TargetLanguage = request.TargetLanguage,
			TranslatedText = dictionary.Translations.FirstOrDefault()?.DisplayTarget ?? string.Empty,
			Dictionary = dictionary
		};
	}

	private async Task<List<DictionaryExample>> TryLoadExamplesAsync(
		string endpoint,
		TranslationRequest request,
		string? normalizedTarget,
		string from,
		string to,
		string key,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(normalizedTarget))
		{
			return [];
		}

		try
		{
			var examplesUri = $"{endpoint}/dictionary/examples?api-version=3.0&from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
			var examples = await PostAzureAsync<AzureExamplesResponse[]>(
				examplesUri,
				request.Settings.AzureRegion,
				key,
				new[] { new AzureDictionaryExampleRequest(request.Text, normalizedTarget) },
				cancellationToken);

			return examples.FirstOrDefault()?.Examples?
				.Select(item => new DictionaryExample
				{
					Source = CombineExample(item.SourcePrefix, item.SourceTerm, item.SourceSuffix),
					Target = CombineExample(item.TargetPrefix, item.TargetTerm, item.TargetSuffix)
				})
				.Where(item => !string.IsNullOrWhiteSpace(item.Source) && !string.IsNullOrWhiteSpace(item.Target))
				.Take(3)
				.ToList() ?? [];
		}
		catch
		{
			return [];
		}
	}

	private async Task<T> PostAzureAsync<T>(
		string uri,
		string region,
		string key,
		object payload,
		CancellationToken cancellationToken)
	{
		using var message = new HttpRequestMessage(HttpMethod.Post, uri);
		message.Headers.Add("Ocp-Apim-Subscription-Key", key);
		if (!string.IsNullOrWhiteSpace(region))
		{
			message.Headers.Add("Ocp-Apim-Subscription-Region", region.Trim());
		}

		message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var response = await httpClient.SendAsync(message, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Bing 词典返回 {(int)response.StatusCode}: {TrimForMessage(body)}");
		}

		return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidOperationException("Bing 词典返回内容无法解析");
	}

	private static bool LooksLikeLongText(string text)
	{
		return text.Length > 90 || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 10 || text.Contains('\n');
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		if (string.IsNullOrWhiteSpace(endpoint))
		{
			return "https://api.cognitive.microsofttranslator.com";
		}

		return endpoint.Trim().TrimEnd('/');
	}

	private static string CombineExample(string? prefix, string? term, string? suffix)
	{
		return $"{prefix}{term}{suffix}".Trim();
	}

	private static string TrimForMessage(string value)
	{
		var normalized = value.ReplaceLineEndings(" ").Trim();
		return normalized.Length <= 220 ? normalized : $"{normalized[..220]}...";
	}

	private sealed record AzureDictionaryLookupRequest([property: JsonPropertyName("Text")] string Text);
	private sealed record AzureDictionaryExampleRequest(
		[property: JsonPropertyName("Text")] string Text,
		[property: JsonPropertyName("Translation")] string Translation);

	private sealed class AzureLookupResponse
	{
		[JsonPropertyName("displaySource")]
		public string? DisplaySource { get; set; }

		[JsonPropertyName("translations")]
		public List<AzureTranslationItem>? Translations { get; set; }
	}

	private sealed class AzureTranslationItem
	{
		[JsonPropertyName("normalizedTarget")]
		public string? NormalizedTarget { get; set; }

		[JsonPropertyName("displayTarget")]
		public string? DisplayTarget { get; set; }

		[JsonPropertyName("posTag")]
		public string? PosTag { get; set; }

		[JsonPropertyName("confidence")]
		public double Confidence { get; set; }

		[JsonPropertyName("backTranslations")]
		public List<AzureBackTranslation>? BackTranslations { get; set; }
	}

	private sealed class AzureBackTranslation
	{
		[JsonPropertyName("normalizedText")]
		public string? NormalizedText { get; set; }

		[JsonPropertyName("displayText")]
		public string? DisplayText { get; set; }
	}

	private sealed class AzureExamplesResponse
	{
		[JsonPropertyName("examples")]
		public List<AzureExampleItem>? Examples { get; set; }
	}

	private sealed class AzureExampleItem
	{
		[JsonPropertyName("sourcePrefix")]
		public string? SourcePrefix { get; set; }

		[JsonPropertyName("sourceTerm")]
		public string? SourceTerm { get; set; }

		[JsonPropertyName("sourceSuffix")]
		public string? SourceSuffix { get; set; }

		[JsonPropertyName("targetPrefix")]
		public string? TargetPrefix { get; set; }

		[JsonPropertyName("targetTerm")]
		public string? TargetTerm { get; set; }

		[JsonPropertyName("targetSuffix")]
		public string? TargetSuffix { get; set; }
	}
}
