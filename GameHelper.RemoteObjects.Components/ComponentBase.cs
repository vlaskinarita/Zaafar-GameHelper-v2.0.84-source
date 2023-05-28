using System;
using GameHelper.Utils;
using GameOffsets.Objects.Components;

namespace GameHelper.RemoteObjects.Components;

public class ComponentBase : RemoteObjectBase
{
	protected IntPtr OwnerEntityAddress;

	public ComponentBase(IntPtr Address)
		: base(Address, forceUpdate: true)
	{
	}

	protected override void CleanUpData()
	{
		throw new Exception("Component Address should never be Zero.");
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		ImGuiHelper.IntPtrToImGui("Owner Address", OwnerEntityAddress);
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		OwnerEntityAddress = Core.Process.Handle.ReadMemory<ComponentHeader>(base.Address).EntityPtr;
	}

	public bool IsParentValid(IntPtr parentEntityAddress)
	{
		return OwnerEntityAddress == parentEntityAddress;
	}
}
