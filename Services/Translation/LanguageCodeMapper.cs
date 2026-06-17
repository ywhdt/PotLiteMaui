namespace PotLiteMaui.Services.Translation;

public static class LanguageCodeMapper
{
	public static string ToGoogle(string code)
	{
		return code switch
		{
			"zh" => "zh-CN",
			_ => string.IsNullOrWhiteSpace(code) ? "auto" : code
		};
	}

}
