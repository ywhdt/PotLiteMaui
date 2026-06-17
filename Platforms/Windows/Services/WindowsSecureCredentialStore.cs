using PotLiteMaui.Services.Platform;
using Windows.Security.Credentials;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsSecureCredentialStore : ISecureCredentialStore
{
	private const string Resource = "PotLiteMaui";
	private readonly PasswordVault _vault = new();

	public Task<string?> GetAsync(string key)
	{
		try
		{
			var credential = _vault.Retrieve(Resource, key);
			credential.RetrievePassword();
			return Task.FromResult<string?>(credential.Password);
		}
		catch
		{
			return Task.FromResult<string?>(null);
		}
	}

	public Task SetAsync(string key, string value)
	{
		DeleteExisting(key);
		if (!string.IsNullOrWhiteSpace(value))
		{
			_vault.Add(new PasswordCredential(Resource, key, value));
		}

		return Task.CompletedTask;
	}

	public Task DeleteAsync(string key)
	{
		DeleteExisting(key);
		return Task.CompletedTask;
	}

	private void DeleteExisting(string key)
	{
		try
		{
			_vault.Remove(_vault.Retrieve(Resource, key));
		}
		catch
		{
			// Missing credentials are fine.
		}
	}
}
