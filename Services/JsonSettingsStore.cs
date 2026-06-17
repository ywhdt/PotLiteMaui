using System.Text.Json;
using PotLiteMaui.Models;

namespace PotLiteMaui.Services;

public sealed class JsonSettingsStore : ISettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	private readonly string _settingsPath;

	public JsonSettingsStore()
	{
		_settingsPath = Path.Combine(FileSystem.AppDataDirectory, "settings.json");
	}

	public async Task<AppSettings> LoadAsync()
	{
		if (!File.Exists(_settingsPath))
		{
			return new AppSettings();
		}

		await using var stream = File.OpenRead(_settingsPath);
		var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions);
		return settings ?? new AppSettings();
	}

	public async Task SaveAsync(AppSettings settings)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
		await using var stream = File.Create(_settingsPath);
		await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
	}
}
