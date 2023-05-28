using System;
using System.Collections.Generic;
using Coroutine;
using GameHelper.Cache;
using GameHelper.CoroutineEvents;
using GameHelper.RemoteEnums;
using GameHelper.RemoteObjects.UiElement;
using GameHelper.Utils;
using GameOffsets.Objects.States.InGameState;

namespace GameHelper.RemoteObjects.States.InGameStateObjects;

public class ImportantUiElements : RemoteObjectBase
{
	internal UiElementParents Parents { get; }

	public LargeMapUiElement LargeMap { get; }

	public MapUiElement MiniMap { get; }

	internal ImportantUiElements(IntPtr address)
		: base(address)
	{
		Parents = new UiElementParents(GameStateTypes.InGameState, GameStateTypes.EscapeState);
		LargeMap = new LargeMapUiElement(IntPtr.Zero);
		MiniMap = new MapUiElement(IntPtr.Zero);
		CoroutineHandler.Start(OnPerFrame(), "", 2147483644);
	}

	internal override void ToImGui()
	{
		Parents.ToImGui();
		base.ToImGui();
	}

	protected override void CleanUpData()
	{
		MiniMap.Address = IntPtr.Zero;
		LargeMap.Address = IntPtr.Zero;
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		Parents.UpdateAllParents();
		SafeMemoryHandle handle = Core.Process.Handle;
		MapParentStruct data2 = handle.ReadMemory<MapParentStruct>(handle.ReadMemory<ImportantUiElementsOffsets>(base.Address).MapParentPtr);
		LargeMap.Address = data2.LargeMapPtr;
		MiniMap.Address = data2.MiniMapPtr;
	}

	private IEnumerator<Wait> OnPerFrame()
	{
		while (true)
		{
			yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
			if (base.Address != IntPtr.Zero && Core.States.GameCurrentState == GameStateTypes.InGameState)
			{
				UpdateData(hasAddressChanged: false);
			}
		}
	}
}
