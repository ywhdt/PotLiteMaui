using System.Text.Json;
using PotLiteMaui.Models;

namespace PotLiteMaui.Services;

public sealed class JsonTranslationHistoryStore : ITranslationHistoryStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	private readonly string _historyPath = Path.Combine(FileSystem.AppDataDirectory, "history.json");

	public async Task<IReadOnlyList<HistoryEntry>> LoadAsync()
	{
		if (!File.Exists(_historyPath))
		{
			return [];
		}

		await using var stream = File.OpenRead(_historyPath);
		var entries = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(stream, JsonOptions);
		return entries?
			.OrderByDescending(entry => entry.CreatedAt)
			.ToArray() ?? [];
	}

	public async Task AddAsync(TranslationBatchResult result, AppSettings settings)
	{
		if (!settings.HistoryEnabled || !result.HasSuccessfulResults)
		{
			return;
		}

		var entries = (await LoadAsync()).ToList();
		entries.Insert(0, HistoryEntry.FromResult(result));
		if (settings.HistoryLimit > 0 && entries.Count > settings.HistoryLimit)
		{
			entries = entries.Take(settings.HistoryLimit).ToList();
		}

		await SaveAsync(entries);
	}

	public async Task DeleteAsync(string id)
	{
		var entries = (await LoadAsync()).Where(entry => entry.Id != id).ToList();
		await SaveAsync(entries);
	}

	public Task ClearAsync()
	{
		if (File.Exists(_historyPath))
		{
			File.Delete(_historyPath);
		}

		return Task.CompletedTask;
	}

	private async Task SaveAsync(List<HistoryEntry> entries)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
		await using var stream = File.Create(_historyPath);
		await JsonSerializer.SerializeAsync(stream, entries, JsonOptions);
	}
}
