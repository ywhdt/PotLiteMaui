using PotLiteMaui.Models;

namespace PotLiteMaui;

public partial class TranslationResultPage : ContentPage
{
	private readonly TranslationResult _result;
	private readonly int _autoHideSeconds;
	private bool _isPinned;

	public TranslationResultPage(TranslationResult result, int autoHideSeconds)
	{
		InitializeComponent();
		_result = result;
		_autoHideSeconds = Math.Clamp(autoHideSeconds, 0, 60);
		ProviderLabel.Text = $"{result.ProviderName}  {result.CreatedAt:HH:mm:ss}";
		SourceLabel.Text = result.SourceText;
		ResultLabel.Text = result.DisplayText;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		if (_autoHideSeconds > 0)
		{
			Dispatcher.StartTimer(TimeSpan.FromSeconds(_autoHideSeconds), () =>
			{
				if (!_isPinned && Window is not null)
				{
					Application.Current?.CloseWindow(Window);
				}

				return false;
			});
		}
	}

	private async void OnCopyClicked(object? sender, EventArgs e)
	{
		await Clipboard.Default.SetTextAsync(_result.DisplayText);
	}

	private void OnPinClicked(object? sender, EventArgs e)
	{
		_isPinned = !_isPinned;
		PinButton.Text = _isPinned ? "取消固定" : "固定";
	}

	private void OnCloseClicked(object? sender, EventArgs e)
	{
		if (Window is not null)
		{
			Application.Current?.CloseWindow(Window);
		}
	}
}
