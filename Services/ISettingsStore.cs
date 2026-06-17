using PotLiteMaui.Models;

namespace PotLiteMaui.Services;

public interface ISettingsStore
{
	Task<AppSettings> LoadAsync();
	Task SaveAsync(AppSettings settings);
}
