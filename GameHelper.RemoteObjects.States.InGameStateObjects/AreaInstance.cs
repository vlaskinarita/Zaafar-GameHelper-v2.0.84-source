using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Coroutine;
using GameHelper.Cache;
using GameHelper.CoroutineEvents;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.FilesStructures;
using GameHelper.Utils;
using GameOffsets.Natives;
using GameOffsets.Objects.States.InGameState;
using ImGuiNET;

namespace GameHelper.RemoteObjects.States.InGameStateObjects;

public class AreaInstance : RemoteObjectBase
{
	private string entityIdFilter;

	private string entityPathFilter;

	private bool filterByPath;

	private StdVector environmentPtr;

	private readonly List<int> environments;

	public int MonsterLevel { get; private set; }

	public string AreaHash { get; private set; }

	public ServerData ServerDataObject { get; }

	public Entity Player { get; }

	public ConcurrentDictionary<EntityNodeKey, Entity> AwakeEntities { get; }

	public List<DisappearingEntity> EntityCaches { get; }

	public int NetworkBubbleEntityCount { get; private set; }

	public TerrainStruct TerrainMetadata { get; private set; }

	public float[][] GridHeightData { get; private set; }

	public byte[] GridWalkableData { get; private set; }

	public Dictionary<string, List<Vector2>> TgtTilesLocations { get; private set; }

	public float WorldToGridConvertor => TileStructure.TileToWorldConversion / (float)TileStructure.TileToGridConversion;

	internal AreaInstance(IntPtr address)
		: base(address)
	{
		entityIdFilter = string.Empty;
		entityPathFilter = string.Empty;
		filterByPath = false;
		environmentPtr = default(StdVector);
		environments = new List<int>();
		MonsterLevel = 0;
		AreaHash = string.Empty;
		ServerDataObject = new ServerData(IntPtr.Zero);
		Player = new Entity();
		AwakeEntities = new ConcurrentDictionary<EntityNodeKey, Entity>();
		EntityCaches = new List<DisappearingEntity>
		{
			new DisappearingEntity("Breach", 1108, 1112, AwakeEntities),
			new DisappearingEntity("LeagueAffliction", 1118, 1118, AwakeEntities),
			new DisappearingEntity("Hellscape", 1248, 1259, AwakeEntities)
		};
		NetworkBubbleEntityCount = 0;
		TerrainMetadata = default(TerrainStruct);
		GridHeightData = Array.Empty<float[]>();
		GridWalkableData = Array.Empty<byte>();
		TgtTilesLocations = new Dictionary<string, List<Vector2>>();
		Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(OnPerFrame(), "[AreaInstance] Update Area Data", 2147483643));
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		if (ImGui.TreeNode("Environment Info"))
		{
			ImGuiHelper.IntPtrToImGui("Address", environmentPtr.First);
			if (ImGui.TreeNode($"All Environments ({environments.Count})###AllEnvironments"))
			{
				for (int i = 0; i < environments.Count; i++)
				{
					if (ImGui.Selectable($"{environments[i]}"))
					{
						ImGui.SetClipboardText($"{environments[i]}");
					}
				}
				ImGui.TreePop();
			}
			foreach (DisappearingEntity entityCache in EntityCaches)
			{
				entityCache.ToImGui();
			}
			ImGui.TreePop();
		}
		ImGui.Text("Area Hash: " + AreaHash);
		ImGui.Text($"Monster Level: {MonsterLevel}");
		if (ImGui.TreeNode("Terrain Metadata"))
		{
			ImGui.Text($"Total Tiles: {TerrainMetadata.TotalTiles}");
			ImGui.Text($"Tiles Data Pointer: {TerrainMetadata.TileDetailsPtr}");
			ImGui.Text($"Tiles Height Multiplier: {TerrainMetadata.TileHeightMultiplier}");
			ImGui.Text($"Grid Walkable Data: {TerrainMetadata.GridWalkableData}");
			ImGui.Text($"Grid Landscape Data: {TerrainMetadata.GridLandscapeData}");
			ImGui.Text($"Data Bytes Per Row (for Walkable/Landscape Data): {TerrainMetadata.BytesPerRow}");
			ImGui.TreePop();
		}
		if (Player.TryGetComponent<Render>(out var pPos))
		{
			int y = (int)pPos.GridPosition.Y;
			int x = (int)pPos.GridPosition.X;
			if (y < GridHeightData.Length && x < GridHeightData[0].Length)
			{
				ImGui.Text("Player Pos to Terrain Height: " + $"{GridHeightData[y][x]}");
			}
		}
		ImGui.Text($"Entities in network bubble: {NetworkBubbleEntityCount}");
		EntitiesWidget("Awake", AwakeEntities);
	}

	protected override void CleanUpData()
	{
		Cleanup(isAreaChange: false);
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		AreaInstanceOffsets data = reader.ReadMemory<AreaInstanceOffsets>(base.Address);
		if (hasAddressChanged)
		{
			Cleanup(isAreaChange: true);
			TerrainMetadata = data.TerrainMetadata;
			MonsterLevel = data.MonsterLevel;
			AreaHash = $"{data.CurrentAreaHash:X}";
			GridWalkableData = reader.ReadStdVector<byte>(TerrainMetadata.GridWalkableData);
			GridHeightData = GetTerrainHeight();
			TgtTilesLocations = GetTgtFileData();
		}
		UpdateEnvironmentAndCaches(data.Environments);
		ServerDataObject.Address = data.ServerDataPtr;
		Player.Address = data.LocalPlayerPtr;
		UpdateEntities(data.AwakeEntities, AwakeEntities, addToCache: true);
	}

	private void UpdateEnvironmentAndCaches(StdVector environments)
	{
		this.environments.Clear();
		SafeMemoryHandle handle = Core.Process.Handle;
		environmentPtr = environments;
		EnvironmentStruct[] envData = handle.ReadStdVector<EnvironmentStruct>(environments);
		for (int i = 0; i < envData.Length; i++)
		{
			this.environments.Add(envData[i].Key);
		}
		EntityCaches.ForEach(delegate(DisappearingEntity eCache)
		{
			eCache.UpdateState(this.environments);
		});
	}

	private void AddToCacheParallel(EntityNodeKey key, string path)
	{
		for (int i = 0; i < EntityCaches.Count && !EntityCaches[i].TryAddParallel(key, path); i++)
		{
		}
	}

	private void UpdateEntities(StdMap ePtr, ConcurrentDictionary<EntityNodeKey, Entity> data, bool addToCache)
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		WorldAreaDat areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
		if (Core.GHSettings.DisableEntityProcessingInTownOrHideout && (areaDetails.IsHideout || areaDetails.IsTown))
		{
			NetworkBubbleEntityCount = 0;
			return;
		}
		List<(EntityNodeKey Key, EntityNodeValue Value)> entities = reader.ReadStdMapAsList<EntityNodeKey, EntityNodeValue>(ePtr, EntityFilter.IgnoreVisualsAndDecorations);
		foreach (KeyValuePair<EntityNodeKey, Entity> kv in data)
		{
			if (!kv.Value.IsValid && (kv.Value.EntityState == EntityStates.MonsterFriendly || (kv.Value.CanExplodeOrRemovedFromGame && Player.DistanceFrom(kv.Value) < 150)))
			{
				data.TryRemove(kv.Key, out var _);
			}
			kv.Value.IsValid = false;
		}
		NetworkBubbleEntityCount = entities.Count;
		Parallel.For(0, entities.Count, delegate(int index)
		{
			var (key, entityNodeValue) = entities[index];
			if (data.TryGetValue(key, out var value2))
			{
				value2.Address = entityNodeValue.EntityPtr;
			}
			else
			{
				value2 = new Entity(entityNodeValue.EntityPtr);
				if (!string.IsNullOrEmpty(value2.Path))
				{
					data[key] = value2;
					if (addToCache)
					{
						AddToCacheParallel(key, value2.Path);
					}
				}
				else
				{
					value2 = null;
				}
			}
			value2?.UpdateNearby(Player);
		});
	}

	private Dictionary<string, List<Vector2>> GetTgtFileData()
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		TileStructure[] tileData = reader.ReadStdVector<TileStructure>(TerrainMetadata.TileDetailsPtr);
		Dictionary<string, List<Vector2>> ret = new Dictionary<string, List<Vector2>>();
		object mylock = new object();
		Parallel.For(0, tileData.Length, () => new Dictionary<string, List<Vector2>>(), delegate(int tileNumber, ParallelLoopState _, Dictionary<string, List<Vector2>> localstate)
		{
			TileStructure tileStructure = tileData[tileNumber];
			TgtFileStruct tgtFileStruct = reader.ReadMemory<TgtFileStruct>(tileStructure.TgtFilePtr);
			string text = reader.ReadStdWString(tgtFileStruct.TgtPath);
			if (string.IsNullOrEmpty(text))
			{
				return localstate;
			}
			text = (((int)tileStructure.RotationSelector % 2 != 0) ? (text + $"x:{tileStructure.tileIdY}-y:{tileStructure.tileIdX}") : (text + $"x:{tileStructure.tileIdX}-y:{tileStructure.tileIdY}"));
			Vector2 vector = default(Vector2);
			vector.Y = tileNumber / TerrainMetadata.TotalTiles.X * TileStructure.TileToGridConversion;
			vector.X = tileNumber % TerrainMetadata.TotalTiles.X * TileStructure.TileToGridConversion;
			Vector2 item = vector;
			if (localstate.ContainsKey(text))
			{
				localstate[text].Add(item);
			}
			else
			{
				localstate[text] = new List<Vector2> { item };
			}
			return localstate;
		}, delegate(Dictionary<string, List<Vector2>> finalresult)
		{
			lock (mylock)
			{
				foreach (KeyValuePair<string, List<Vector2>> current in finalresult)
				{
					if (!ret.ContainsKey(current.Key))
					{
						ret[current.Key] = new List<Vector2>();
					}
					ret[current.Key].AddRange(current.Value);
				}
			}
		});
		return ret;
	}

	private float[][] GetTerrainHeight()
	{
		byte[] rotationHelper = Core.RotationSelector.Values;
		byte[] rotatorMetrixHelper = Core.RotatorHelper.Values;
		SafeMemoryHandle reader = Core.Process.Handle;
		TileStructure[] tileData = reader.ReadStdVector<TileStructure>(TerrainMetadata.TileDetailsPtr);
		ConcurrentDictionary<IntPtr, sbyte[]> subTileHeightCache = new ConcurrentDictionary<IntPtr, sbyte[]>();
		Parallel.For(0, tileData.Length, delegate(int index)
		{
			TileStructure tileStructure2 = tileData[index];
			subTileHeightCache.AddOrUpdate(tileStructure2.SubTileDetailsPtr, delegate(IntPtr addr)
			{
				SubTileStruct subTileStruct = reader.ReadMemory<SubTileStruct>(addr);
				return reader.ReadStdVector<sbyte>(subTileStruct.SubTileHeight);
			}, (IntPtr addr, sbyte[] data) => data);
		});
		int gridSizeX = (int)TerrainMetadata.TotalTiles.X * TileStructure.TileToGridConversion;
		int gridSizeY = (int)TerrainMetadata.TotalTiles.Y * TileStructure.TileToGridConversion;
		float[][] result = new float[gridSizeY][];
		Parallel.For(0, gridSizeY, delegate(int y)
		{
			result[y] = new float[gridSizeX];
			for (int i = 0; i < gridSizeX; i++)
			{
				int num = y / TileStructure.TileToGridConversion * (int)TerrainMetadata.TotalTiles.X;
				num += i / TileStructure.TileToGridConversion;
				int num2 = 0;
				if (num < tileData.Length)
				{
					TileStructure tileStructure = tileData[num];
					if (subTileHeightCache.TryGetValue(tileStructure.SubTileDetailsPtr, out var value))
					{
						int num3 = i % TileStructure.TileToGridConversion;
						int num4 = y % TileStructure.TileToGridConversion;
						int num5 = ((tileStructure.RotationSelector < rotationHelper.Length) ? (rotationHelper[tileStructure.RotationSelector] * 3) : 24);
						num5 = ((num5 > 24) ? 24 : num5);
						int[] obj = new int[4]
						{
							TileStructure.TileToGridConversion - num3 - 1,
							num3,
							TileStructure.TileToGridConversion - num4 - 1,
							num4
						};
						int num6 = rotatorMetrixHelper[num5];
						int num7 = rotatorMetrixHelper[num5 + 1];
						int num8 = rotatorMetrixHelper[num5 + 2];
						int num9 = 0;
						if (num6 == 0)
						{
							num9 = 2;
						}
						int x = obj[num6 * 2 + num7];
						int y2 = obj[num8 + num9];
						num2 = GetSubTerrainHeight(value, y2, x);
						result[y][i] = (float)tileStructure.TileHeight * (float)TerrainMetadata.TileHeightMultiplier + (float)num2;
						result[y][i] = result[y][i] * TerrainStruct.TileHeightFinalMultiplier * -1f;
					}
				}
			}
		});
		return result;
	}

	private int GetSubTerrainHeight(sbyte[] subterrainheightarray, int y, int x)
	{
		if (x < 0 || y < 0 || x >= TileStructure.TileToGridConversion || y >= TileStructure.TileToGridConversion)
		{
			return 0;
		}
		_ = TileStructure.TileToGridConversion;
		_ = subterrainheightarray.LongLength;
		return 0;
	}

	private void Cleanup(bool isAreaChange)
	{
		AwakeEntities.Clear();
		EntityCaches.ForEach(delegate(DisappearingEntity e)
		{
			e.Clear();
		});
		if (!isAreaChange)
		{
			environmentPtr = default(StdVector);
			environments.Clear();
			MonsterLevel = 0;
			AreaHash = string.Empty;
			ServerDataObject.Address = IntPtr.Zero;
			Player.Address = IntPtr.Zero;
			NetworkBubbleEntityCount = 0;
			TerrainMetadata = default(TerrainStruct);
			GridHeightData = Array.Empty<float[]>();
			GridWalkableData = Array.Empty<byte>();
			TgtTilesLocations.Clear();
		}
	}

	private void EntitiesWidget(string label, ConcurrentDictionary<EntityNodeKey, Entity> data)
	{
		if (!ImGui.TreeNode($"{label} Entities ({data.Count})###${label} Entities"))
		{
			return;
		}
		if (ImGui.RadioButton("Filter by Id           ", !filterByPath))
		{
			filterByPath = false;
			entityPathFilter = string.Empty;
		}
		ImGui.SameLine();
		if (ImGui.RadioButton("Filter by Path", filterByPath))
		{
			filterByPath = true;
			entityIdFilter = string.Empty;
		}
		if (filterByPath)
		{
			ImGui.InputText("Entity Path Filter", ref entityPathFilter, 100u);
		}
		else
		{
			ImGui.InputText("Entity Id Filter", ref entityIdFilter, 10u, ImGuiInputTextFlags.CharsDecimal);
		}
		foreach (KeyValuePair<EntityNodeKey, Entity> entity in data)
		{
			if ((string.IsNullOrEmpty(entityIdFilter) || $"{entity.Key.id}".Contains(entityIdFilter)) && (string.IsNullOrEmpty(entityPathFilter) || entity.Value.Path.ToLower().Contains(entityPathFilter.ToLower())))
			{
				if (ImGui.TreeNode($"{entity.Value.Id} {entity.Value.Path}"))
				{
					entity.Value.ToImGui();
					ImGui.TreePop();
				}
				if (entity.Value.IsValid && entity.Value.TryGetComponent<Render>(out var eRender))
				{
					ImGuiHelper.DrawText(eRender.WorldPosition, $"ID: {entity.Key.id}");
				}
			}
		}
		ImGui.TreePop();
	}

	private IEnumerator<Wait> OnPerFrame()
	{
		while (true)
		{
			yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
			if (base.Address != IntPtr.Zero)
			{
				UpdateData(hasAddressChanged: false);
			}
		}
	}
}
