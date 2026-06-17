namespace PotLiteMaui.Models;

public enum TranslationResultKind
{
	Translation,
	Dictionary
}

public sealed record TranslationProviderDescriptor(
	string Id,
	string DisplayName,
	bool IsDictionary,
	bool IsExperimental);

public sealed record TranslationRequest(
	string Text,
	string SourceLanguage,
	string TargetLanguage,
	string ProviderId,
	AppSettings Settings);

public sealed class TranslationResult
{
	public string Id { get; init; } = Guid.NewGuid().ToString("N");
	public TranslationResultKind Kind { get; init; } = TranslationResultKind.Translation;
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public string SourceText { get; init; } = string.Empty;
	public string SourceLanguage { get; init; } = string.Empty;
	public string TargetLanguage { get; init; } = string.Empty;
	public string TranslatedText { get; init; } = string.Empty;
	public DictionaryResult? Dictionary { get; init; }
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

	public string DisplayText => Kind == TranslationResultKind.Dictionary && Dictionary is not null
		? Dictionary.ToDisplayText()
		: TranslatedText;
}

public sealed class DictionaryResult
{
	public string Term { get; init; } = string.Empty;
	public List<DictionaryTranslation> Translations { get; init; } = [];
	public List<DictionaryExample> Examples { get; init; } = [];

	public string ToDisplayText()
	{
		var lines = new List<string>();
		foreach (var translation in Translations.Take(8))
		{
			var pos = string.IsNullOrWhiteSpace(translation.PartOfSpeech) ? string.Empty : $" [{translation.PartOfSpeech}]";
			lines.Add($"{translation.DisplayTarget}{pos}");
			if (translation.BackTranslations.Count > 0)
			{
				lines.Add($"  {string.Join(", ", translation.BackTranslations.Take(5))}");
			}
		}

		if (Examples.Count > 0)
		{
			lines.Add(string.Empty);
			lines.Add("例句");
			foreach (var example in Examples.Take(3))
			{
				lines.Add($"- {example.Source}");
				lines.Add($"  {example.Target}");
			}
		}

		return lines.Count == 0 ? "没有词典结果" : string.Join(Environment.NewLine, lines);
	}
}

public sealed class DictionaryTranslation
{
	public string DisplayTarget { get; init; } = string.Empty;
	public string PartOfSpeech { get; init; } = string.Empty;
	public double Confidence { get; init; }
	public List<string> BackTranslations { get; init; } = [];
}

public sealed class DictionaryExample
{
	public string Source { get; init; } = string.Empty;
	public string Target { get; init; } = string.Empty;
}

public sealed class HistoryEntry
{
	public string Id { get; init; } = Guid.NewGuid().ToString("N");
	public TranslationResultKind Kind { get; init; }
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public string SourceText { get; init; } = string.Empty;
	public string ResultText { get; init; } = string.Empty;
	public string SourceLanguage { get; init; } = string.Empty;
	public string TargetLanguage { get; init; } = string.Empty;
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

	public string DisplayTitle
	{
		get
		{
			var source = SourceText.ReplaceLineEndings(" ").Trim();
			if (source.Length > 42)
			{
				source = $"{source[..42]}...";
			}

			return $"{CreatedAt:MM-dd HH:mm}  {ProviderName}  {source}";
		}
	}

	public static HistoryEntry FromResult(TranslationResult result)
	{
		return new HistoryEntry
		{
			Id = result.Id,
			Kind = result.Kind,
			ProviderId = result.ProviderId,
			ProviderName = result.ProviderName,
			SourceText = result.SourceText,
			ResultText = result.DisplayText,
			SourceLanguage = result.SourceLanguage,
			TargetLanguage = result.TargetLanguage,
			CreatedAt = result.CreatedAt
		};
	}
}
