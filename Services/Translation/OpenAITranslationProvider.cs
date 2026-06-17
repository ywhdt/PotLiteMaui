using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PotLiteMaui.Models;
using PotLiteMaui.Services.Platform;

namespace PotLiteMaui.Services.Translation;

public sealed class OpenAITranslationProvider(
	HttpClient httpClient,
	ISecureCredentialStore credentialStore) : ITranslationProvider
{
	private const string OpenAIEndpoint = "https://api.openai.com/v1/chat/completions";

	public TranslationProviderDescriptor Descriptor { get; } = new(
		TranslationProviderIds.OpenAI,
		"OpenAI",
		IsDictionary: false,
		IsExperimental: false);

	public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
	{
		var key = await credentialStore.GetAsync(SecureCredentialKeys.OpenAIApiKey);
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new InvalidOperationException("请先填写 OpenAI API Key");
		}

		var model = string.IsNullOrWhiteSpace(request.Settings.OpenAIModel)
			? "gpt-4o-mini"
			: request.Settings.OpenAIModel.Trim();
		var messages = CreateMessages(request);
		var payload = new
		{
			model,
			temperature = 0.2,
			messages
		};

		using var message = new HttpRequestMessage(HttpMethod.Post, OpenAIEndpoint);
		message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
		message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
		using var response = await httpClient.SendAsync(message, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"OpenAI 返回 {(int)response.StatusCode}: {TrimForMessage(body)}");
		}

		var translated = ExtractOpenAIText(body);
		if (string.IsNullOrWhiteSpace(translated))
		{
			throw new InvalidOperationException("OpenAI 没有返回译文");
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

	private static OpenAIMessage[] CreateMessages(TranslationRequest request)
	{
		if (!string.IsNullOrWhiteSpace(request.Settings.OpenAICustomPrompt))
		{
			return
			[
				new("system", request.Settings.OpenAICustomPrompt.Trim()),
				new("user", request.Text)
			];
		}

		return
		[
			new("system", "Translate the user's text. Return only the translated text, with no explanations."),
			new("user", $"Source language: {request.SourceLanguage}\nTarget language: {request.TargetLanguage}\n\n{request.Text}")
		];
	}

	private static string? ExtractOpenAIText(string body)
	{
		using var doc = JsonDocument.Parse(body);
		if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
		{
			return null;
		}

		var choice = choices[0];
		if (!choice.TryGetProperty("message", out var message))
		{
			return null;
		}

		return message.TryGetProperty("content", out var content) ? content.GetString() : null;
	}

	private static string TrimForMessage(string value)
	{
		var normalized = value.ReplaceLineEndings(" ").Trim();
		return normalized.Length <= 220 ? normalized : $"{normalized[..220]}...";
	}

	private sealed record OpenAIMessage(
		[property: JsonPropertyName("role")] string Role,
		[property: JsonPropertyName("content")] string Content);
}
