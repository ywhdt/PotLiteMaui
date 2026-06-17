using System.Text.Json;
using PotLiteMaui.Models;

namespace PotLiteMaui.Services.Translation;

public sealed class GoogleWebTranslateProvider(HttpClient httpClient) : ITranslationProvider
{
	public TranslationProviderDescriptor Descriptor { get; } = new(
		TranslationProviderIds.GoogleWeb,
		"Google 翻译",
		IsDictionary: false,
		IsExperimental: true);

	public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
	{
		var sourceLanguage = LanguageCodeMapper.ToGoogle(request.SourceLanguage);
		var targetLanguage = LanguageCodeMapper.ToGoogle(request.TargetLanguage);
		var url = "https://translate.googleapis.com/translate_a/single" +
			$"?client=gtx&sl={Uri.EscapeDataString(sourceLanguage)}&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(request.Text)}";

		using var message = new HttpRequestMessage(HttpMethod.Get, url);
		message.Headers.UserAgent.ParseAdd("Mozilla/5.0 PotLiteMaui/1.0");
		using var response = await httpClient.SendAsync(message, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Google 翻译返回 {(int)response.StatusCode}: {TrimForMessage(body)}");
		}

		var translated = ExtractGoogleText(body);
		if (string.IsNullOrWhiteSpace(translated))
		{
			throw new InvalidOperationException("Google 翻译没有返回译文");
		}

		return new TranslationResult
		{
			ProviderId = Descriptor.Id,
			ProviderName = Descriptor.DisplayName,
			SourceText = request.Text,
			SourceLanguage = request.SourceLanguage,
			TargetLanguage = request.TargetLanguage,
			TranslatedText = translated.Trim()
		};
	}

	private static string ExtractGoogleText(string body)
	{
		using var document = JsonDocument.Parse(body);
		if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
		{
			return string.Empty;
		}

		var segments = document.RootElement[0];
		if (segments.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}

		var pieces = new List<string>();
		foreach (var segment in segments.EnumerateArray())
		{
			if (segment.ValueKind == JsonValueKind.Array &&
				segment.GetArrayLength() > 0 &&
				segment[0].ValueKind == JsonValueKind.String)
			{
				pieces.Add(segment[0].GetString() ?? string.Empty);
			}
		}

		return string.Concat(pieces);
	}

	private static string TrimForMessage(string value)
	{
		var normalized = value.ReplaceLineEndings(" ").Trim();
		return normalized.Length <= 220 ? normalized : $"{normalized[..220]}...";
	}
}
