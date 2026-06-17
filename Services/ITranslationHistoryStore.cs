using PotLiteMaui.Models;

namespace PotLiteMaui.Services;

public interface ITranslationHistoryStore
{
	Task<IReadOnlyList<HistoryEntry>> LoadAsync();
	Task AddAsync(TranslationBatchResult result, AppSettings settings);
	Task DeleteAsync(string id);
	Task ClearAsync();
}
