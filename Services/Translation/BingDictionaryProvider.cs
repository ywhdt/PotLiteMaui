using System.Net;
using System.Text.RegularExpressions;
using PotLiteMaui.Models;

namespace PotLiteMaui.Services.Translation;

public sealed partial class BingDictionaryProvider(HttpClient httpClient) : ITranslationProvider
{
	private const string BingDictionaryBaseUrl = "https://cn.bing.com";

	public TranslationProviderDescriptor Descriptor { get; } = new(
		TranslationProviderIds.BingDictionary,
		"Bing 词典",
		IsDictionary: true,
		IsExperimental: true);

	public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
	{
		if (LooksLikeLongText(request.Text))
		{
			throw new InvalidOperationException("Bing 词典网页适合单词或短语，请切换到 Google 或 OpenAI 翻译长句");
		}

		var lookupUrl = BuildLookupUrl(request.Text);
		using var message = new HttpRequestMessage(HttpMethod.Get, lookupUrl);
		message.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
		message.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");

		using var response = await httpClient.SendAsync(message, cancellationToken);
		var html = await response.Content.ReadAsStringAsync(cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"Bing 词典网页返回 {(int)response.StatusCode}: {TrimForMessage(html)}");
		}

		var dictionary = ParseDictionary(html, request.Text, lookupUrl);
		if (dictionary.Translations.Count == 0 && dictionary.Pronunciations.Count == 0)
		{
			throw new InvalidOperationException("Bing 词典网页没有返回词典结果");
		}

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

	private static DictionaryResult ParseDictionary(string html, string fallbackTerm, string lookupUrl)
	{
		return new DictionaryResult
		{
			Term = ExtractHeadword(html, fallbackTerm),
			Pronunciations = ExtractPronunciations(html).ToList(),
			Translations = ExtractSummaryTranslations(html).ToList(),
			Examples = ExtractExamples(html).ToList(),
			SourceUrl = lookupUrl
		};
	}

	private static string BuildLookupUrl(string text)
	{
		return $"{BingDictionaryBaseUrl}/dict/search?q={Uri.EscapeDataString(text.Trim())}&setlang=zh-cn&mkt=zh-cn";
	}

	private static string ExtractHeadword(string html, string fallbackTerm)
	{
		var match = HeadwordRegex().Match(html);
		var term = match.Success ? CleanText(match.Groups["term"].Value) : string.Empty;
		return string.IsNullOrWhiteSpace(term) ? fallbackTerm.Trim() : term;
	}

	private static IEnumerable<DictionaryPronunciation> ExtractPronunciations(string html)
	{
		foreach (Match match in PronunciationRegex().Matches(html))
		{
			var rawText = CleanText(match.Groups["text"].Value);
			var audioUrl = NormalizeBingUrl(WebUtility.HtmlDecode(match.Groups["audio"].Value));
			if (string.IsNullOrWhiteSpace(rawText) || string.IsNullOrWhiteSpace(audioUrl))
			{
				continue;
			}

			var label = rawText.StartsWith("美", StringComparison.OrdinalIgnoreCase)
				? "美音"
				: rawText.StartsWith("英", StringComparison.OrdinalIgnoreCase)
					? "英音"
					: "发音";
			var phonetic = rawText
				.Replace("美", string.Empty, StringComparison.OrdinalIgnoreCase)
				.Replace("英", string.Empty, StringComparison.OrdinalIgnoreCase)
				.Trim();

			yield return new DictionaryPronunciation
			{
				Label = label,
				Phonetic = phonetic,
				AudioUrl = audioUrl
			};
		}
	}

	private static IEnumerable<DictionaryTranslation> ExtractSummaryTranslations(string html)
	{
		var summary = ExtractFirstSummaryList(html);
		var source = string.IsNullOrWhiteSpace(summary) ? html : summary;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match match in SummaryDefinitionRegex().Matches(source))
		{
			var pos = CleanText(match.Groups["pos"].Value);
			var definition = CleanText(match.Groups["definition"].Value);
			if (string.IsNullOrWhiteSpace(definition) || !seen.Add($"{pos}|{definition}"))
			{
				continue;
			}

			yield return new DictionaryTranslation
			{
				DisplayTarget = definition,
				PartOfSpeech = pos
			};
		}

		if (seen.Count > 0)
		{
			yield break;
		}

		foreach (Match match in DetailedDefinitionRegex().Matches(html))
		{
			var definition = CleanText(match.Groups["definition"].Value);
			var english = CleanText(match.Groups["english"].Value);
			if (string.IsNullOrWhiteSpace(definition) || !seen.Add(definition))
			{
				continue;
			}

			yield return new DictionaryTranslation
			{
				DisplayTarget = definition,
				BackTranslations = string.IsNullOrWhiteSpace(english) ? [] : [english]
			};
		}
	}

	private static string ExtractFirstSummaryList(string html)
	{
		var qdefIndex = html.IndexOf("class=\"qdef\"", StringComparison.OrdinalIgnoreCase);
		if (qdefIndex < 0)
		{
			return string.Empty;
		}

		var ulStart = html.IndexOf("<ul>", qdefIndex, StringComparison.OrdinalIgnoreCase);
		if (ulStart < 0)
		{
			return string.Empty;
		}

		var ulEnd = html.IndexOf("</ul>", ulStart, StringComparison.OrdinalIgnoreCase);
		return ulEnd < 0 ? string.Empty : html[ulStart..(ulEnd + "</ul>".Length)];
	}

	private static IEnumerable<DictionaryExample> ExtractExamples(string html)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Match match in ExampleRegex().Matches(html))
		{
			var source = CleanText(match.Groups["source"].Value);
			var target = CleanText(match.Groups["target"].Value);
			if (string.IsNullOrWhiteSpace(source) ||
				string.IsNullOrWhiteSpace(target) ||
				!seen.Add($"{source}|{target}"))
			{
				continue;
			}

			yield return new DictionaryExample
			{
				Source = source,
				Target = target
			};

			if (seen.Count >= 4)
			{
				yield break;
			}
		}
	}

	private static string NormalizeBingUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return string.Empty;
		}

		url = url.Trim();
		if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
		{
			return absoluteUri.ToString();
		}

		if (Uri.TryCreate(new Uri(BingDictionaryBaseUrl), url, out var relativeUri))
		{
			return relativeUri.ToString();
		}

		return string.Empty;
	}

	private static string CleanText(string html)
	{
		if (string.IsNullOrWhiteSpace(html))
		{
			return string.Empty;
		}

		var text = ScriptStyleRegex().Replace(html, " ");
		text = TagRegex().Replace(text, " ");
		text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
		text = WhitespaceRegex().Replace(text, " ").Trim();
		text = Regex.Replace(text, @"\s+([,.;:!?，。；：！？])", "$1");
		return text;
	}

	private static bool LooksLikeLongText(string text)
	{
		return text.Length > 120 || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 14 || text.Contains('\n');
	}

	private static string TrimForMessage(string value)
	{
		var normalized = value.ReplaceLineEndings(" ").Trim();
		return normalized.Length <= 220 ? normalized : $"{normalized[..220]}...";
	}

	[GeneratedRegex("""<div\s+class="hd_div"\s+id="headword"[^>]*>\s*<h1>\s*<strong>(?<term>.*?)</strong>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex HeadwordRegex();

	[GeneratedRegex("<div\\s+class=\"(?<kind>hd_prUS|hd_pr)[^\"]*\"[^>]*>(?<text>.*?)</div>\\s*<div\\s+class=\"hd_tf\"[^>]*>\\s*<a[^>]*data-mp3link=\"(?<audio>[^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex PronunciationRegex();

	[GeneratedRegex("""<li>\s*<span\s+class="pos[^"]*"[^>]*>(?<pos>.*?)</span>\s*<span\s+class="def[^"]*"[^>]*>(?<definition>.*?)</span>\s*</li>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex SummaryDefinitionRegex();

	[GeneratedRegex("""<span\s+class="bil[^"]*"[^>]*>(?<definition>.*?)</span>(?:\s*<span\s+class="val[^"]*"[^>]*>(?<english>.*?)</span>)?""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex DetailedDefinitionRegex();

	[GeneratedRegex("""<div\s+class="li_ex"[^>]*>\s*<div\s+class="val_ex"[^>]*>(?<source>.*?)</div>\s*<div\s+class="bil_ex"[^>]*>(?<target>.*?)</div>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex ExampleRegex();

	[GeneratedRegex("""<script.*?</script>|<style.*?</style>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex ScriptStyleRegex();

	[GeneratedRegex("""<[^>]+>""", RegexOptions.Singleline)]
	private static partial Regex TagRegex();

	[GeneratedRegex(@"\s+")]
	private static partial Regex WhitespaceRegex();
}
