using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GameHelper.RemoteEnums.Entity;
using GameHelper.RemoteObjects.Components;
using GameHelper.Utils;
using GameOffsets.Objects.States.InGameState;
using ImGuiNET;

namespace GameHelper.RemoteObjects.States.InGameStateObjects;

public class Entity : RemoteObjectBase
{
	private static readonly string DeliriumHiddenMonsterStarting = "Metadata/Monsters/LeagueAffliction/DoodadDaemons/DoodadDaemon";

	private readonly ConcurrentDictionary<string, IntPtr> componentAddresses;

	private readonly ConcurrentDictionary<string, ComponentBase> componentCache;

	private bool isnearby;

	public string Path { get; private set; }

	public uint Id { get; private set; }

	public bool IsNearby
	{
		get
		{
			if (IsValid)
			{
				return isnearby;
			}
			return false;
		}
	}

	public bool IsValid { get; set; }

	public EntityTypes EntityType { get; protected set; }

	public EntitySubtypes EntitySubtype { get; protected set; }

	public EntityStates EntityState { get; protected set; }

	public bool CanExplodeOrRemovedFromGame
	{
		get
		{
			if (EntityState != EntityStates.Useless && (EntityType != EntityTypes.Monster || EntityState == EntityStates.LegionStage1Dead))
			{
				return EntityType == EntityTypes.POIMonster;
			}
			return true;
		}
	}

	internal Entity(IntPtr address)
		: this()
	{
		base.Address = address;
	}

	internal Entity()
		: base(IntPtr.Zero, forceUpdate: true)
	{
		componentAddresses = new ConcurrentDictionary<string, IntPtr>();
		componentCache = new ConcurrentDictionary<string, ComponentBase>();
		isnearby = false;
		Path = string.Empty;
		Id = 0u;
		IsValid = false;
		EntityType = EntityTypes.Unidentified;
		EntitySubtype = EntitySubtypes.Unidentified;
		EntityState = EntityStates.None;
	}

	public int DistanceFrom(Entity other)
	{
		if (TryGetComponent<Render>(out var myPosComp) && other.TryGetComponent<Render>(out var otherPosComp))
		{
			float num = myPosComp.GridPosition.X - otherPosComp.GridPosition.X;
			float dy = myPosComp.GridPosition.Y - otherPosComp.GridPosition.Y;
			return (int)Math.Sqrt(num * num + dy * dy);
		}
		return 0;
	}

	public bool TryGetComponent<T>(out T component) where T : ComponentBase
	{
		component = null;
		string componenName = typeof(T).Name;
		if (componentCache.TryGetValue(componenName, out var comp))
		{
			component = (T)comp;
			return true;
		}
		if (componentAddresses.TryGetValue(componenName, out var compAddr) && compAddr != IntPtr.Zero)
		{
			component = Activator.CreateInstance(typeof(T), compAddr) as T;
			if (component != null)
			{
				componentCache[componenName] = component;
				return true;
			}
		}
		return false;
	}

	internal override void ToImGui()
	{
		base.ToImGui();
		ImGui.Text("Path: " + Path);
		ImGui.Text($"Id: {Id}");
		ImGui.Text($"Is Valid: {IsValid}");
		ImGui.Text($"Entity Type: {EntityType}");
		ImGui.Text($"Entity SubType: {EntitySubtype}");
		ImGui.Text($"Entity State: {EntityState}");
		if (!ImGui.TreeNode("Components"))
		{
			return;
		}
		foreach (KeyValuePair<string, IntPtr> kv in componentAddresses)
		{
			if (componentCache.ContainsKey(kv.Key))
			{
				if (ImGui.TreeNode(kv.Key ?? ""))
				{
					componentCache[kv.Key].ToImGui();
					ImGui.TreePop();
				}
				continue;
			}
			Type componentType = Type.GetType(typeof(NPC).Namespace + "." + kv.Key);
			if (componentType != null)
			{
				if (ImGui.SmallButton("Load##" + kv.Key))
				{
					LoadComponent(componentType);
				}
				ImGui.SameLine();
			}
			ImGuiHelper.IntPtrToImGui(kv.Key, kv.Value);
		}
		ImGui.TreePop();
	}

	internal void UpdateNearby(Entity player)
	{
		if (EntityState == EntityStates.Useless || DistanceFrom(player) >= Core.GHSettings.NearbyMeaning)
		{
			isnearby = false;
		}
		else
		{
			isnearby = true;
		}
	}

	protected bool UpdateComponentData(ItemStruct idata, bool hasAddressChanged)
	{
		SafeMemoryHandle reader = Core.Process.Handle;
		if (hasAddressChanged)
		{
			componentAddresses.Clear();
			componentCache.Clear();
			IntPtr[] entityComponent = reader.ReadStdVector<IntPtr>(idata.ComponentListPtr);
			EntityDetails entityDetails = reader.ReadMemory<EntityDetails>(idata.EntityDetailsPtr);
			Path = reader.ReadStdWString(entityDetails.name);
			List<ComponentNameAndIndexStruct> namesAndIndexes = reader.ReadStdBucket<ComponentNameAndIndexStruct>(reader.ReadMemory<ComponentLookUpStruct>(entityDetails.ComponentLookUpPtr).ComponentsNameAndIndex);
			for (int i = 0; i < namesAndIndexes.Count; i++)
			{
				ComponentNameAndIndexStruct nameAndIndex = namesAndIndexes[i];
				if (nameAndIndex.Index >= 0 && nameAndIndex.Index < entityComponent.Length)
				{
					string name = reader.ReadString(nameAndIndex.NamePtr);
					if (!string.IsNullOrEmpty(name))
					{
						componentAddresses.TryAdd(name, entityComponent[nameAndIndex.Index]);
					}
				}
			}
		}
		else
		{
			foreach (KeyValuePair<string, ComponentBase> kv in componentCache)
			{
				kv.Value.Address = kv.Value.Address;
				if (!kv.Value.IsParentValid(base.Address))
				{
					return false;
				}
			}
		}
		return true;
	}

	protected override void CleanUpData()
	{
		componentAddresses?.Clear();
		componentCache?.Clear();
	}

	protected override void UpdateData(bool hasAddressChanged)
	{
		EntityOffsets entityData = Core.Process.Handle.ReadMemory<EntityOffsets>(base.Address);
		IsValid = EntityHelper.IsValidEntity(entityData.IsValid);
		if (!IsValid)
		{
			return;
		}
		Id = entityData.Id;
		if (EntityState != EntityStates.Useless)
		{
			if (!UpdateComponentData(entityData.ItemBase, hasAddressChanged))
			{
				UpdateComponentData(entityData.ItemBase, hasAddressChanged: true);
			}
			if (EntityType == EntityTypes.Unidentified && !TryCalculateEntityType())
			{
				EntityState = EntityStates.Useless;
			}
			else if (EntitySubtype == EntitySubtypes.Unidentified && !TryCalculateEntitySubType())
			{
				EntityState = EntityStates.Useless;
			}
			else
			{
				CalculateEntityState();
			}
		}
	}

	private void LoadComponent(Type componentType)
	{
		if (componentAddresses.TryGetValue(componentType.Name, out var compAddr) && compAddr != IntPtr.Zero && Activator.CreateInstance(componentType, compAddr) is ComponentBase component)
		{
			componentCache[componentType.Name] = component;
		}
	}

	private bool TryCalculateEntityType()
	{
		if (!TryGetComponent<Render>(out var _))
		{
			return false;
		}
		Player component3;
		Shrine component4;
		if (TryGetComponent<Chest>(out var _))
		{
			EntityType = EntityTypes.Chest;
		}
		else if (TryGetComponent<Player>(out component3))
		{
			EntityType = EntityTypes.Player;
		}
		else if (TryGetComponent<Shrine>(out component4))
		{
			EntityType = EntityTypes.Shrine;
		}
		else
		{
			if (!TryGetComponent<Life>(out var _))
			{
				return false;
			}
			if (TryGetComponent<TriggerableBlockage>(out var _))
			{
				EntityType = EntityTypes.Blockage;
			}
			else
			{
				if (!TryGetComponent<Positioned>(out var pos))
				{
					return false;
				}
				if (!TryGetComponent<ObjectMagicProperties>(out var _))
				{
					return false;
				}
				if (!pos.IsFriendly && TryGetComponent<DiesAfterTime>(out var _))
				{
					if (!TryGetComponent<Targetable>(out var tComp) || !tComp.IsTargetable)
					{
						return false;
					}
					EntityType = EntityTypes.Monster;
				}
				else
				{
					if (!TryGetComponent<Buffs>(out var buffComp))
					{
						return false;
					}
					if (Core.GHSettings.PoiMonsterPaths.Any((string k) => Path.StartsWith(k)))
					{
						EntityType = EntityTypes.POIMonster;
					}
					else if (buffComp.StatusEffects.ContainsKey("hidden_monster"))
					{
						if (Path.StartsWith(DeliriumHiddenMonsterStarting))
						{
							if (Path.Contains("BloodBag"))
							{
								EntityType = EntityTypes.DeliriumBomb;
							}
							else if (Path.Contains("EggFodder"))
							{
								EntityType = EntityTypes.DeliriumSpawner;
							}
							else
							{
								if (!Path.Contains("GlobSpawn"))
								{
									return false;
								}
								EntityType = EntityTypes.DeliriumSpawner;
							}
						}
						else
						{
							EntityType = EntityTypes.Monster;
						}
					}
					else
					{
						EntityType = EntityTypes.Monster;
					}
				}
			}
		}
		return true;
	}

	private bool TryCalculateEntitySubType()
	{
		switch (EntityType)
		{
		case EntityTypes.Unidentified:
			throw new Exception($"Entity with path ({Path}) and Id (${Id}) is unidentified.");
		case EntityTypes.Chest:
		{
			TryGetComponent<Chest>(out var chestComp);
			if (Path.StartsWith("Metadata/Chests/LeaguesExpedition"))
			{
				EntitySubtype = EntitySubtypes.ExpeditionChest;
				break;
			}
			if (TryGetComponent<MinimapIcon>(out var _))
			{
				return false;
			}
			if (Path.StartsWith("Metadata/Chests/LegionChests"))
			{
				return false;
			}
			if (Path.StartsWith("Metadata/Chests/DelveChests/"))
			{
				EntitySubtype = EntitySubtypes.DelveChest;
			}
			else if (Path.StartsWith("Metadata/Chests/Breach"))
			{
				EntitySubtype = EntitySubtypes.BreachChest;
			}
			else if (chestComp.IsStrongbox || Path.StartsWith("Metadata/Chests/SynthesisChests/SynthesisChestAmbush"))
			{
				if (Path.StartsWith("Metadata/Chests/StrongBoxes/Arcanist") || Path.StartsWith("Metadata/Chests/StrongBoxes/Cartographer") || Path.StartsWith("Metadata/Chests/StrongBoxes/StrongboxDivination") || Path.StartsWith("Metadata/Chests/StrongBoxes/StrongboxScarab"))
				{
					EntitySubtype = EntitySubtypes.ImportantStrongbox;
				}
				else
				{
					EntitySubtype = EntitySubtypes.Strongbox;
				}
			}
			else if (chestComp.IsLabelVisible)
			{
				EntitySubtype = EntitySubtypes.ChestWithLabel;
			}
			else
			{
				EntitySubtype = EntitySubtypes.None;
			}
			break;
		}
		case EntityTypes.Player:
			if (Id == Core.States.InGameStateObject.CurrentAreaInstance.Player.Id)
			{
				EntitySubtype = EntitySubtypes.PlayerSelf;
			}
			else
			{
				EntitySubtype = EntitySubtypes.PlayerOther;
			}
			break;
		case EntityTypes.Monster:
		{
			if (TryGetComponent<Buffs>(out var buffComp))
			{
				if (buffComp.StatusEffects.ContainsKey("metamorphosis_monster_visual"))
				{
					EntitySubtype = EntitySubtypes.MetamorphMonster;
				}
				else if (buffComp.StatusEffects.ContainsKey("frozen_in_time"))
				{
					if (buffComp.StatusEffects.ContainsKey("legion_reward_display"))
					{
						EntitySubtype = EntitySubtypes.LegionChest;
					}
					else if (Path.Contains("ChestEpic"))
					{
						EntitySubtype = EntitySubtypes.LegionEpicChest;
					}
					else if (Path.Contains("Chest"))
					{
						EntitySubtype = EntitySubtypes.LegionChest;
					}
					else
					{
						EntitySubtype = EntitySubtypes.LegionMonster;
					}
				}
				else
				{
					EntitySubtype = EntitySubtypes.None;
				}
			}
			else
			{
				EntitySubtype = EntitySubtypes.None;
			}
			break;
		}
		case EntityTypes.Item:
			EntitySubtype = EntitySubtypes.WorldItem;
			break;
		case EntityTypes.POIMonster:
			EntitySubtype = EntitySubtypes.None;
			break;
		default:
			throw new Exception($"Please update TryCalculateEntitySubType function to include {EntityType}.");
		case EntityTypes.Shrine:
		case EntityTypes.Blockage:
		case EntityTypes.DeliriumBomb:
		case EntityTypes.DeliriumSpawner:
			break;
		}
		return true;
	}

	private void CalculateEntityState()
	{
		Player playerComp;
		if (EntityType == EntityTypes.Chest)
		{
			if (TryGetComponent<Chest>(out var chestComp) && chestComp.IsOpened)
			{
				EntityState = EntityStates.Useless;
			}
		}
		else if (EntityType == EntityTypes.DeliriumBomb || EntityType == EntityTypes.DeliriumSpawner)
		{
			if (TryGetComponent<Life>(out var lifeComp) && !lifeComp.IsAlive)
			{
				EntityState = EntityStates.Useless;
			}
		}
		else if (EntityType == EntityTypes.POIMonster)
		{
			if (TryGetComponent<Life>(out var lifeComp3) && !lifeComp3.IsAlive)
			{
				EntityState = EntityStates.Useless;
			}
		}
		else if (EntityType == EntityTypes.Monster)
		{
			if (!TryGetComponent<Life>(out var lifeComp2))
			{
				return;
			}
			if (!lifeComp2.IsAlive)
			{
				EntityState = EntityStates.Useless;
			}
			else
			{
				if (!TryGetComponent<Positioned>(out var posComp))
				{
					return;
				}
				Buffs buffComp;
				if (posComp.IsFriendly)
				{
					EntityState = EntityStates.MonsterFriendly;
				}
				else if (EntityState == EntityStates.MonsterFriendly)
				{
					EntityState = EntityStates.None;
				}
				else if ((EntitySubtype == EntitySubtypes.LegionChest || EntitySubtype == EntitySubtypes.LegionEpicChest || EntitySubtype == EntitySubtypes.LegionMonster) && TryGetComponent<Buffs>(out buffComp))
				{
					bool isFrozenInTime = buffComp.StatusEffects.ContainsKey("frozen_in_time");
					bool isHidden = buffComp.StatusEffects.ContainsKey("hidden_monster");
					if (isFrozenInTime && isHidden)
					{
						EntityState = EntityStates.LegionStage0;
					}
					else if (isFrozenInTime)
					{
						EntityState = EntityStates.LegionStage1Alive;
					}
					else if (isHidden)
					{
						EntityState = EntityStates.LegionStage1Dead;
					}
					else
					{
						EntityState = EntityStates.None;
					}
				}
			}
		}
		else if (EntitySubtype == EntitySubtypes.PlayerOther && TryGetComponent<Player>(out playerComp))
		{
			if (playerComp.Name.Equals(Core.GHSettings.LeaderName))
			{
				EntityState = EntityStates.PlayerLeader;
			}
			else
			{
				EntityState = EntityStates.None;
			}
		}
	}
}
