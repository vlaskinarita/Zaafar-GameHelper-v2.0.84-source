using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using ClickableTransparentOverlay;
using ClickableTransparentOverlay.Win32;
using Coroutine;
using GameHelper.CoroutineEvents;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States;
using GameHelper.Utils;
using GameOffsets.Natives;
using GameOffsets.Objects.States.InGameState;
using ImGuiNET;

namespace GameHelper.Settings;

internal static class SettingsWindow
{
	private static Vector4 color = new Vector4(1f, 1f, 0f, 1f);

	private static bool isOverlayRunningLocal = true;

	private static bool isSettingsWindowVisible = true;

	private static string poiMonstersString = string.Empty;

	private static Vector2 textBoxSize = new Vector2(800f, 200f);

	private static bool loadpoi = true;

	internal static void InitializeCoroutines()
	{
		HideOnStartCheck();
		CoroutineHandler.Start(SaveCoroutine());
		Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(RenderCoroutine(), "[Settings] Draw Core/Plugin settings", int.MaxValue));
	}

	private static void DrawManuBar()
	{
		if (!ImGui.BeginMenuBar())
		{
			return;
		}
		if (ImGui.BeginMenu("Enable Plugins"))
		{
			foreach (PluginContainer container in PManager.Plugins)
			{
				bool isEnabled = container.Metadata.Enable;
				if (ImGui.Checkbox(container.Name ?? "", ref isEnabled))
				{
					container.Metadata.Enable = !container.Metadata.Enable;
					if (container.Metadata.Enable)
					{
						container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
						continue;
					}
					container.Plugin.SaveSettings();
					container.Plugin.OnDisable();
				}
			}
			ImGui.EndMenu();
		}
		if (ImGui.MenuItem("Donate (捐)"))
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = "https://www.paypal.com/paypalme/Ghelper",
				UseShellExecute = true
			});
		}
		ImGui.EndMenuBar();
	}

	private static void DrawTabs()
	{
		if (!ImGui.BeginTabBar("pluginsTabBar", ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs))
		{
			return;
		}
		if (ImGui.BeginTabItem("Core"))
		{
			if (ImGui.BeginChild("CoreChildSetting"))
			{
				DrawCoreSettings();
				ImGui.EndChild();
			}
			ImGui.EndTabItem();
		}
		foreach (PluginContainer container in PManager.Plugins)
		{
			if (container.Metadata.Enable && ImGui.BeginTabItem(container.Name))
			{
				if (ImGui.BeginChild("PluginChildSetting"))
				{
					container.Plugin.DrawSettings();
					ImGui.EndChild();
				}
				ImGui.EndTabItem();
			}
		}
		ImGui.EndTabBar();
	}

	private static void DrawCoreSettings()
	{
		ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X);
		ImGui.TextColored(color, "This is a free software, only use https://ownedcore.com to download it. Do not buy from the fake sellers or websites.");
		ImGui.TextColored(color, "请不要花钱购买本软件，否则你就是个傻逼。这是一个免费软件。不要从假卖家那里购买。前往 https://ownedcore.com 免费下载。");
		ImGui.NewLine();
		ImGui.TextColored(Vector4.One, "Developer of this software is not responsible for any loss that may happen due to the usage of this software. Use this software at your own risk.");
		ImGui.NewLine();
		ImGui.TextColored(Vector4.One, "All Settings (including plugins) are saved automatically " + $"when you close the overlay or hide it via {Core.GHSettings.MainMenuHotKey} button.");
		ImGui.NewLine();
		ImGui.PopTextWrapPos();
		PoiWidget();
		ImGui.NewLine();
		ImGui.DragInt("Nearby Monster Range", ref Core.GHSettings.NearbyMeaning, 1f, 1, 200);
		DrawNearbyMonsterRange();
		ImGuiHelper.ToolTip("If you are in the game and hovering over this, it will draw your current nearby range on the screen.");
		ImGui.DragInt("Key Timeout", ref Core.GHSettings.KeyPressTimeout, 0.2f, 60, 300);
		ImGuiHelper.ToolTip("When GameOverlay press a key in the game, the key has to go to the GGG server for it to work. This process takes time equal to your latency x 3. During this time GameOverlay might press that key again. Set the key timeout value to latency x 3 so this doesn't happen. e.g. for 30ms latency, set it to 90ms. Also, do not go below 60 (due to server ticks), no matter how good your latency is.");
		ImGui.NewLine();
		ImGui.Text($"Current Game State: {Core.States.GameCurrentState}");
		ImGui.NewLine();
		ImGui.InputText("Party Leader Name", ref Core.GHSettings.LeaderName, 200u);
		ImGuiHelper.NonContinuousEnumComboBox("Settings Window Key", ref Core.GHSettings.MainMenuHotKey);
		ImGuiHelper.NonContinuousEnumComboBox("Disable Rendering Key", ref Core.GHSettings.DisableAllRenderingKey);
		ImGui.NewLine();
		ImGui.Checkbox("Performance Stats", ref Core.GHSettings.ShowPerfStats);
		if (Core.GHSettings.ShowPerfStats)
		{
			ImGui.Spacing();
			ImGui.SameLine();
			ImGui.Spacing();
			ImGui.SameLine();
			ImGui.Checkbox("Hide when game is in background", ref Core.GHSettings.HidePerfStatsWhenBg);
		}
		ImGui.Checkbox("Game UiExplorer (GE)", ref Core.GHSettings.ShowGameUiExplorer);
		ImGui.Checkbox("Data Visualization (DV)", ref Core.GHSettings.ShowDataVisualization);
		ImGui.Checkbox("Disable entity processing when in town or hideout", ref Core.GHSettings.DisableEntityProcessingInTownOrHideout);
		ImGui.Checkbox("Hide overlay settings upon start", ref Core.GHSettings.HideSettingWindowOnStart);
		ImGui.Checkbox("Close GameHelper when Game Exit", ref Core.GHSettings.CloseWhenGameExit);
		if (ImGui.Checkbox("V-Sync", ref Core.Overlay.VSync))
		{
			Core.GHSettings.Vsync = Core.Overlay.VSync;
		}
		ImGuiHelper.ToolTip("WARNING: There is no rate limiter in GameHelper, once V-Sync is off,\nit's your responsibility to use external rate limiter e.g. NVIDIA Control Panel\n-> Manage 3D Settings -> Set Max Framerate to what your monitor support.");
		ImGui.NewLine();
		ChangeFontWidget();
		ImGui.NewLine();
		if (ImGui.Button("Test Disconnect POE"))
		{
			MiscHelper.KillTCPConnectionForProcess(Core.Process.Pid);
		}
	}

	private static void ChangeFontWidget()
	{
		if (!ImGui.CollapsingHeader("Change Fonts"))
		{
			return;
		}
		ImGui.InputText("Pathname", ref Core.GHSettings.FontPathName, 300u);
		ImGui.DragInt("Size", ref Core.GHSettings.FontSize, 0.1f, 13, 40);
		bool num = ImGuiHelper.EnumComboBox("Language", ref Core.GHSettings.FontLanguage);
		bool customLanguage = ImGui.InputText("Custom Glyph Ranges", ref Core.GHSettings.FontCustomGlyphRange, 100u);
		ImGuiHelper.ToolTip("This is advance level feature. Do not modify this if you don't know what you are doing. Example usage:- If you have downloaded and pointed to the ArialUnicodeMS.ttf font, you can use 0x0020, 0xFFFF, 0x00 text in this field to load all of the font texture in ImGui. Note the 0x00 as the last item in the range.");
		if (num)
		{
			Core.GHSettings.FontCustomGlyphRange = string.Empty;
		}
		if (customLanguage)
		{
			Core.GHSettings.FontLanguage = FontGlyphRangeType.English;
		}
		if (ImGui.Button("Apply Changes"))
		{
			if (MiscHelper.TryConvertStringToImGuiGlyphRanges(Core.GHSettings.FontCustomGlyphRange, out var glyphranges))
			{
				Core.Overlay.ReplaceFont(Core.GHSettings.FontPathName, Core.GHSettings.FontSize, glyphranges);
			}
			else
			{
				Core.Overlay.ReplaceFont(Core.GHSettings.FontPathName, Core.GHSettings.FontSize, Core.GHSettings.FontLanguage);
			}
		}
	}

	private static void PoiWidget()
	{
		bool num = ImGui.CollapsingHeader("Special Monster Tracker (A.K.A POI Monster)");
		ImGuiHelper.ToolTip("Add 1 Metadata path per line. In order to figure out the path to add please open DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> Select the entity you want to add -> Path.");
		if (!num)
		{
			return;
		}
		if (loadpoi)
		{
			poiMonstersString = string.Join("\n", Core.GHSettings.PoiMonsterPaths);
			loadpoi = false;
		}
		textBoxSize.X = ImGui.GetContentRegionAvail().X;
		ImGui.InputTextMultiline("##POI Monsters Path", ref poiMonstersString, 10000u, textBoxSize);
		bool num2 = ImGui.Button("Apply Changes");
		ImGuiHelper.ToolTip("Require Area Change after pressing the apply button.");
		if (num2)
		{
			Core.GHSettings.PoiMonsterPaths = poiMonstersString.Split(new string[2] { "\n", "@" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
			Core.GHSettings.PoiMonsterPaths.RemoveAll((string k) => k.Length < 5);
			poiMonstersString = string.Join("\n", Core.GHSettings.PoiMonsterPaths);
		}
	}

	private static void DrawConfirmationPopup()
	{
		ImGui.SetNextWindowPos(new Vector2((float)Core.Overlay.Size.Width / 3f, (float)Core.Overlay.Size.Height / 3f));
		if (ImGui.BeginPopup("GameHelperCloseConfirmation"))
		{
			ImGui.Text("Do you want to quit the GameHelper overlay?");
			ImGui.Separator();
			if (ImGui.Button("Yes", new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2f)))
			{
				Core.GHSettings.IsOverlayRunning = false;
				ImGui.CloseCurrentPopup();
				isOverlayRunningLocal = true;
			}
			ImGui.SameLine();
			if (ImGui.Button("No", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2f)))
			{
				ImGui.CloseCurrentPopup();
				isOverlayRunningLocal = true;
			}
			ImGui.EndPopup();
		}
	}

	private static void HideOnStartCheck()
	{
		if (Core.GHSettings.HideSettingWindowOnStart)
		{
			isSettingsWindowVisible = false;
		}
	}

	private static IEnumerator<Wait> RenderCoroutine()
	{
		while (true)
		{
			yield return new Wait(GameHelperEvents.OnRender);
			if (ClickableTransparentOverlay.Win32.Utils.IsKeyPressedAndNotTimeout(Core.GHSettings.MainMenuHotKey))
			{
				isSettingsWindowVisible = !isSettingsWindowVisible;
				ImGui.GetIO().WantCaptureMouse = true;
				if (!isSettingsWindowVisible)
				{
					CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
				}
			}
			if (isSettingsWindowVisible)
			{
				ImGui.SetNextWindowSizeConstraints(new Vector2(800f, 600f), Vector2.One * float.MaxValue);
				bool num = ImGui.Begin("Game Overlay Settings [ " + Core.GetVersion() + " ]", ref isOverlayRunningLocal, ImGuiWindowFlags.MenuBar);
				if (!isOverlayRunningLocal)
				{
					ImGui.OpenPopup("GameHelperCloseConfirmation");
				}
				DrawConfirmationPopup();
				if (!Core.GHSettings.IsOverlayRunning)
				{
					CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
				}
				if (!num)
				{
					ImGui.End();
					continue;
				}
				DrawManuBar();
				DrawTabs();
				ImGui.End();
			}
		}
	}

	private static IEnumerator<Wait> SaveCoroutine()
	{
		while (true)
		{
			yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
			JsonHelper.SafeToFile(Core.GHSettings, State.CoreSettingFile);
		}
	}

	private static void DrawNearbyMonsterRange()
	{
		InGameState iGS = Core.States.InGameStateObject;
		if (!ImGui.IsItemHovered() || Core.States.GameCurrentState != GameStateTypes.InGameState || !iGS.CurrentAreaInstance.Player.TryGetComponent<Render>(out var r))
		{
			return;
		}
		foreach (int item in Enumerable.Range(0, 360))
		{
			Vector2 p1 = GetScreenCoord(item);
			Vector2 p2 = GetScreenCoord(item + 1);
			ImGui.GetBackgroundDrawList().AddLine(p1, p2, ImGuiHelper.Color(255u, 0u, 0u, 255u));
		}
		Vector2 GetScreenCoord(int i)
		{
			Vector2 gridPoint = new Vector2(r.GridPosition.X, r.GridPosition.Y) + new Vector2((float)(Math.Cos(Math.PI / 180.0 * (double)i) * (double)Core.GHSettings.NearbyMeaning), (float)(Math.Sin(Math.PI / 180.0 * (double)i) * (double)Core.GHSettings.NearbyMeaning));
			float height = r.TerrainHeight;
			try
			{
				height = Core.States.InGameStateObject.CurrentAreaInstance.GridHeightData[(int)gridPoint.Y][(int)gridPoint.X];
			}
			catch (Exception)
			{
			}
			return Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(new StdTuple3D<float>
			{
				X = gridPoint.X * TileStructure.TileToWorldConversion / (float)TileStructure.TileToGridConversion,
				Y = gridPoint.Y * TileStructure.TileToWorldConversion / (float)TileStructure.TileToGridConversion,
				Z = height
			});
		}
	}
}
