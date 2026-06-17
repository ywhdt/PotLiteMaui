#if WINDOWS
using Microsoft.Maui.Storage;
using PotLiteMaui.Services.Platform;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace PotLiteMaui.Platforms.Windows.Services;

public sealed class WindowsAudioPlaybackService : IAudioPlaybackService, IDisposable
{
	private readonly MediaPlayer _mediaPlayer = new();
	private readonly HttpClient _httpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(20)
	};
	private string? _currentAudioPath;

	public WindowsAudioPlaybackService()
	{
		_mediaPlayer.MediaEnded += (_, _) => CleanupCurrentAudio();
		_mediaPlayer.MediaFailed += (_, _) => CleanupCurrentAudio();
		CleanupAudioCache();
	}

	public async Task PlayAsync(string audioUrl, CancellationToken cancellationToken = default)
	{
		if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException("发音地址无效");
		}

		_mediaPlayer.Pause();
		_mediaPlayer.Source = null;
		CleanupCurrentAudio();
		var audioPath = await DownloadAudioAsync(uri, cancellationToken);
		_currentAudioPath = audioPath;
		_mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(audioPath));
		_mediaPlayer.Play();
	}

	public void Dispose()
	{
		CleanupCurrentAudio();
		_mediaPlayer.Dispose();
		_httpClient.Dispose();
	}

	private async Task<string> DownloadAudioAsync(Uri uri, CancellationToken cancellationToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, uri);
		request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125 Safari/537.36");
		request.Headers.Referrer = new Uri("https://cn.bing.com/dict/search");
		request.Headers.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");

		using var response = await _httpClient.SendAsync(request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			throw new InvalidOperationException($"发音下载失败：{(int)response.StatusCode}");
		}

		var directory = Path.Combine(FileSystem.CacheDirectory, "bing-dict-audio");
		Directory.CreateDirectory(directory);

		var filePath = Path.Combine(directory, $"{Guid.NewGuid():N}.mp3");
		await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using var file = File.Create(filePath);
		await stream.CopyToAsync(file, cancellationToken);
		return filePath;
	}

	private void CleanupCurrentAudio()
	{
		var audioPath = _currentAudioPath;
		_currentAudioPath = null;
		if (string.IsNullOrWhiteSpace(audioPath))
		{
			return;
		}

		try
		{
			_mediaPlayer.Source = null;
			if (File.Exists(audioPath))
			{
				File.Delete(audioPath);
			}
		}
		catch
		{
			// Cache cleanup is best-effort; playback should not fail because of it.
		}
	}

	private static void CleanupAudioCache()
	{
		try
		{
			var directory = Path.Combine(FileSystem.CacheDirectory, "bing-dict-audio");
			if (!Directory.Exists(directory))
			{
				return;
			}

			foreach (var file in Directory.EnumerateFiles(directory, "*.mp3"))
			{
				try
				{
					File.Delete(file);
				}
				catch
				{
					// Another process may still hold a file. It can be retried next launch.
				}
			}
		}
		catch
		{
			// Cache cleanup is best-effort.
		}
	}
}
#endif
