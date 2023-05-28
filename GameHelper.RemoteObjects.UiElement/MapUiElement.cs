using System;
using System.Numerics;
using GameOffsets.Objects.UiElement;
using ImGuiNET;

namespace GameHelper.RemoteObjects.UiElement;

public class MapUiElement : UiElementBase
{
	private Vector2 defaultShift = Vector2.Zero;

	private Vector2 shift = Vector2.Zero;

	public Vector2 Shift => shift;

	public Vector2 DefaultShift => defaultShift;

	public float Zoom { get; private set; } = 0.5f;


	internal MapUiElement(IntPtr address)
		: base(address)
	{
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		ImGui.Text($"Shift {shift}");
		ImGui.Text($"Default Shift {defaultShift}");
		ImGui.Text($"Zoom {Zoom}");
	}

	protected override void CleanUpData()
	{
		base.CleanUpData();
		shift = default(Vector2);
		defaultShift = default(Vector2);
		Zoom = 0.5f;
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		MapUiElementOffset data = Core.Process.Handle.ReadMemory<MapUiElementOffset>(base.Address);
		UpdateData(data.UiElementBase, hasAddressChanged);
		shift.X = data.Shift.X;
		shift.Y = data.Shift.Y;
		defaultShift.X = data.DefaultShift.X;
		defaultShift.Y = data.DefaultShift.Y;
		Zoom = data.Zoom;
	}
}
