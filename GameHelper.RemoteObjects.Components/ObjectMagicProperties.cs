using System;
using System.Collections.Generic;
using GameHelper.RemoteEnums;
using GameHelper.Utils;
using GameOffsets.Natives;
using GameOffsets.Objects.Components;
using ImGuiNET;

namespace GameHelper.RemoteObjects.Components;

public class ObjectMagicProperties : ComponentBase
{
	public List<(string name, (float value0, float value1) values)> Mods = new List<(string, (float, float))>();

	public Rarity Rarity { get; private set; }

	public ObjectMagicProperties(IntPtr address)
		: base(address)
	{
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		ImGui.Text($"Rarity: {Rarity}");
		ModsToImGui("All Mods", Mods);
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		ObjectMagicPropertiesOffsets data = reader.ReadMemory<ObjectMagicPropertiesOffsets>(base.Address);
		OwnerEntityAddress = data.Header.EntityPtr;
		Rarity = (Rarity)data.Details.Rarity;
		if (hasAddressChanged)
		{
			AddToMods(Mods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.ImplicitMods));
			AddToMods(Mods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.ExplicitMods));
			AddToMods(Mods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.EnchantMods));
			AddToMods(Mods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.HellscapeMods));
			AddToMods(Mods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.CrucibleMods));
		}
	}

	internal static void ModsToImGui(string text, List<(string name, (float value0, float value1) values)> collection)
	{
		if (ImGui.TreeNode(text))
		{
			for (int i = 0; i < collection.Count; i++)
			{
				var (name, values) = collection[i];
				ImGuiHelper.DisplayTextAndCopyOnClick($"{name}: {values.Item1} - {values.Item2}", name);
			}
			ImGui.TreePop();
		}
	}

	internal static void AddToMods(List<(string name, (float value0, float value1) values)> collection, ModArrayStruct[] mods)
	{
		for (int i = 0; i < mods.Length; i++)
		{
			ModArrayStruct mod = mods[i];
			if (mod.ModsPtr != IntPtr.Zero)
			{
				collection.Add((GetModName(mod.ModsPtr), GetValue(mod.Values, mod.Value0)));
			}
		}
	}

	internal static string GetModName(IntPtr modsDatRowAddress)
	{
		return Core.GgpkStringCache.AddOrGetExisting(modsDatRowAddress, delegate(IntPtr key)
		{
			SafeMemoryHandle handle = Core.Process.Handle;
			return handle.ReadUnicodeString(handle.ReadMemory<IntPtr>(key));
		});
	}

	internal static (float, float) GetValue(StdVector valuesPtr, int value0)
	{
		switch (valuesPtr.TotalElements(4))
		{
		case 0L:
			return (float.NaN, float.NaN);
		case 1L:
			return (value0, float.NaN);
		default:
		{
			int[] values = Core.Process.Handle.ReadStdVector<int>(valuesPtr);
			return (values[0], values[1]);
		}
		}
	}
}
