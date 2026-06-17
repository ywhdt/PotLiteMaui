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

public sealed record TranslationBatchRequest(
	string Text,
	string SourceLanguage,
	string TargetLanguage,
	IReadOnlyList<string> ProviderIds,
	AppSettings Settings);

public sealed class TranslationResult
{
	public string Id { get; init; } = Guid.NewGuid().ToString("N");
	public TranslationResultKind Kind { get; init; } = TranslationResultKind.Translation;
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public int Order { get; set; }
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

public sealed class TranslationProviderFailure
{
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public int Order { get; init; }
	public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class TranslationDisplayItem
{
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public int Order { get; init; }
	public bool IsSuccess { get; init; }
	public string Text { get; init; } = string.Empty;
}

public sealed class TranslationBatchResult
{
	public string Id { get; init; } = Guid.NewGuid().ToString("N");
	public string SourceText { get; init; } = string.Empty;
	public string SourceLanguage { get; init; } = string.Empty;
	public string TargetLanguage { get; init; } = string.Empty;
	public List<TranslationResult> Results { get; init; } = [];
	public List<TranslationProviderFailure> Failures { get; init; } = [];
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

	public bool HasSuccessfulResults => Results.Count > 0;

	public IEnumerable<TranslationDisplayItem> DisplayItems => Results
		.Select(result => new TranslationDisplayItem
		{
			ProviderId = result.ProviderId,
			ProviderName = result.ProviderName,
			Order = result.Order,
			IsSuccess = true,
			Text = result.DisplayText
		})
		.Concat(Failures.Select(failure => new TranslationDisplayItem
		{
			ProviderId = failure.ProviderId,
			ProviderName = failure.ProviderName,
			Order = failure.Order,
			IsSuccess = false,
			Text = failure.ErrorMessage
		}))
		.OrderBy(item => item.Order);

	public string ProviderName
	{
		get
		{
			var count = Results.Count + Failures.Count;
			if (count <= 1)
			{
				return Results.FirstOrDefault()?.ProviderName
					?? Failures.FirstOrDefault()?.ProviderName
					?? string.Empty;
			}

			return $"多引擎 ({Results.Count}/{count})";
		}
	}

	public string DisplayText
	{
		get
		{
			var sections = new List<string>();
			foreach (var item in DisplayItems)
			{
				var title = item.IsSuccess ? item.ProviderName : $"{item.ProviderName} 失败";
				sections.Add($"[{title}]\n{item.Text}");
			}

			return sections.Count == 0 ? "没有翻译结果" : string.Join("\n\n", sections);
		}
	}
}

public sealed class DictionaryResult
{
	public string Term { get; init; } = string.Empty;
	public List<DictionaryTranslation> Translations { get; init; } = [];
	public List<DictionaryExample> Examples { get; init; } = [];
	public List<DictionaryPronunciation> Pronunciations { get; init; } = [];
	public string SourceUrl { get; init; } = string.Empty;

	public string ToDisplayText()
	{
		var lines = new List<string>();
		if (!string.IsNullOrWhiteSpace(Term))
		{
			lines.Add(Term);
		}

		if (Pronunciations.Count > 0)
		{
			var phonetics = Pronunciations
				.Select(item => item.Phonetic)
				.Where(item => !string.IsNullOrWhiteSpace(item))
				.ToArray();
			if (phonetics.Length > 0)
			{
				lines.Add(string.Join("  ", phonetics));
				lines.Add(string.Empty);
			}
		}

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

public sealed class DictionaryPronunciation
{
	public string Label { get; init; } = string.Empty;
	public string Phonetic { get; init; } = string.Empty;
	public string AudioUrl { get; init; } = string.Empty;
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
	public List<HistoryResultItem> Results { get; init; } = [];
	public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

	public string DisplayTitle
	{
		get
		{
			var source = SourceText.ReplaceLineEndings(" ").Trim();
			if (source.Length > 60)
			{
				source = $"{source[..60]}...";
			}

			return string.IsNullOrWhiteSpace(source) ? "(空文本)" : source;
		}
	}

	public string DisplaySubtitle => $"{CreatedAt:MM-dd HH:mm}  {ProviderName}";

	public static HistoryEntry FromResult(TranslationBatchResult result)
	{
		return new HistoryEntry
		{
			Id = result.Id,
			Kind = result.Results.FirstOrDefault()?.Kind ?? TranslationResultKind.Translation,
			ProviderId = result.Results.Count == 1 && result.Failures.Count == 0
				? result.Results[0].ProviderId
				: "Multiple",
			ProviderName = result.ProviderName,
			SourceText = result.SourceText,
			ResultText = result.DisplayText,
			SourceLanguage = result.SourceLanguage,
			TargetLanguage = result.TargetLanguage,
			Results = result.DisplayItems.Select(item => new HistoryResultItem
			{
				ProviderId = item.ProviderId,
				ProviderName = item.ProviderName,
				Order = item.Order,
				IsSuccess = item.IsSuccess,
				Text = item.Text
			}).ToList(),
			CreatedAt = result.CreatedAt
		};
	}

	public override string ToString() => DisplayTitle;
}

public sealed class HistoryResultItem
{
	public string ProviderId { get; init; } = string.Empty;
	public string ProviderName { get; init; } = string.Empty;
	public int Order { get; init; }
	public bool IsSuccess { get; init; }
	public string Text { get; init; } = string.Empty;
}
