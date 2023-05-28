using System;
using System.Collections.Generic;
using GameHelper.RemoteEnums;
using GameHelper.Utils;
using GameOffsets.Objects.Components;

namespace GameHelper.RemoteObjects.Components;

public class Mods : ComponentBase
{
	public List<(string name, (float value0, float value1) values)> ImplicitMods = new List<(string, (float, float))>();

	public List<(string name, (float value0, float value1) values)> ExplicitMods = new List<(string, (float, float))>();

	public List<(string name, (float value0, float value1) values)> EnchantMods = new List<(string, (float, float))>();

	public List<(string name, (float value0, float value1) values)> HellscapeMods = new List<(string, (float, float))>();

	public Rarity Rarity { get; private set; }

	public Mods(IntPtr address)
		: base(address)
	{
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		ObjectMagicProperties.ModsToImGui("ImplicitMods", ImplicitMods);
		ObjectMagicProperties.ModsToImGui("ExplicitMods", ExplicitMods);
		ObjectMagicProperties.ModsToImGui("EnchantMods", EnchantMods);
		ObjectMagicProperties.ModsToImGui("HellscapeMods", HellscapeMods);
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		ModsOffsets data = reader.ReadMemory<ModsOffsets>(base.Address);
		OwnerEntityAddress = data.Header.EntityPtr;
		Rarity = (Rarity)data.Details.Rarity;
		if (hasAddressChanged)
		{
			ObjectMagicProperties.AddToMods(ImplicitMods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.ImplicitMods));
			ObjectMagicProperties.AddToMods(ExplicitMods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.ExplicitMods));
			ObjectMagicProperties.AddToMods(EnchantMods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.EnchantMods));
			ObjectMagicProperties.AddToMods(HellscapeMods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.HellscapeMods));
			ObjectMagicProperties.AddToMods(HellscapeMods, reader.ReadStdVector<ModArrayStruct>(data.Details.Mods.CrucibleMods));
		}
	}
}
