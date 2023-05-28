using System;
using System.Collections.Generic;
using Coroutine;
using GameHelper.CoroutineEvents;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.UiElement;
using ImGuiNET;

namespace GameHelper.Cache;

internal class UiElementParents
{
	private readonly GameStateTypes ownerState1;

	private readonly GameStateTypes ownerState2;

	private readonly Dictionary<IntPtr, UiElementBase> cache;

	public UiElementParents(GameStateTypes ownerStateA, GameStateTypes ownerStateB)
	{
		ownerState1 = ownerStateA;
		ownerState2 = ownerStateB;
		cache = new Dictionary<IntPtr, UiElementBase>();
		CoroutineHandler.Start(OnGameClose());
		CoroutineHandler.Start(OnStateChange());
	}

	public void AddIfNotExists(IntPtr address)
	{
		if (address != IntPtr.Zero && !cache.ContainsKey(address))
		{
			try
			{
				cache.Add(address, new UiElementBase(address));
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to add the UiElement Parent in the cache. 0x{address.ToInt64():X} due to {e}");
			}
		}
	}

	public UiElementBase GetParent(IntPtr address)
	{
		return cache[address];
	}

	public void UpdateAllParents()
	{
		foreach (var (key, value) in cache)
		{
			try
			{
				value.Address = key;
			}
			catch (Exception e)
			{
				Console.WriteLine($"Failed to update the UiElement Parent in the cache. 0x{key.ToInt64():X} due to {e}");
			}
		}
	}

	public void ToImGui()
	{
		ImGui.Text($"Total Size: {cache.Count}");
		if (!ImGui.TreeNode("Parent UiElements"))
		{
			return;
		}
		foreach (var (key, value) in cache)
		{
			if (ImGui.TreeNode($"0x{key.ToInt64():X}"))
			{
				value.ToImGui();
				ImGui.TreePop();
			}
		}
		ImGui.TreePop();
	}

	private IEnumerable<Wait> OnGameClose()
	{
		while (true)
		{
			yield return new Wait(GameHelperEvents.OnClose);
			cache.Clear();
		}
	}

	private IEnumerable<Wait> OnStateChange()
	{
		while (true)
		{
			yield return new Wait(RemoteEvents.StateChanged);
			if (Core.States.GameCurrentState != ownerState1 && Core.States.GameCurrentState != ownerState2)
			{
				cache.Clear();
			}
		}
	}
}
