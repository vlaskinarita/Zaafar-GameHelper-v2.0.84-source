using System.Collections.Generic;
using System.IO;
using ClickableTransparentOverlay;
using ClickableTransparentOverlay.Win32;
using Newtonsoft.Json;

namespace GameHelper.Settings;

internal class State
{
	[JsonIgnore]
	public static readonly FileInfo CoreSettingFile = new FileInfo("configs/core_settings.json");

	[JsonIgnore]
	public static readonly FileInfo PluginsMetadataFile = new FileInfo("configs/plugins.json");

	[JsonIgnore]
	public static readonly DirectoryInfo PluginsDirectory = new DirectoryInfo("Plugins");

	public bool DisableEntityProcessingInTownOrHideout;

	public bool HidePerfStatsWhenBg;

	public bool HideSettingWindowOnStart;

	[JsonIgnore]
	public bool IsOverlayRunning = true;

	public int KeyPressTimeout = 80;

	public string FontPathName = "C:\\Windows\\Fonts\\msyh.ttc";

	public int FontSize = 18;

	public FontGlyphRangeType FontLanguage = FontGlyphRangeType.ChineseSimplifiedCommon;

	public string FontCustomGlyphRange = string.Empty;

	public VK MainMenuHotKey = VK.F12;

	public bool ShowDataVisualization;

	public bool ShowGameUiExplorer;

	public bool ShowPerfStats;

	public int NearbyMeaning = 70;

	public bool SkipPreloadedFilesInHideout = true;

	public bool CloseWhenGameExit;

	public bool Vsync = true;

	public string LeaderName = string.Empty;

	public VK DisableAllRenderingKey = VK.F9;

	public List<string> PoiMonsterPaths = new List<string>();
}
