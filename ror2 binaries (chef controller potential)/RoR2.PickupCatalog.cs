// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupCatalog
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using HG;
using JetBrains.Annotations;
using RoR2;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;

public static class PickupCatalog
{
	public struct Enumerator : IEnumerator<PickupIndex>, IEnumerator, IDisposable
	{
		private PickupIndex position;

		public PickupIndex Current => position;

		object IEnumerator.Current => Current;

		public bool MoveNext()
		{
			++position;
			return position.value < pickupCount;
		}

		public void Reset()
		{
			position = PickupIndex.none;
		}

		void IDisposable.Dispose()
		{
		}
	}

	private static PickupDef[] entries = Array.Empty<PickupDef>();

	private static PickupIndex[] itemIndexToPickupIndex = Array.Empty<PickupIndex>();

	private static PickupIndex[] equipmentIndexToPickupIndex = Array.Empty<PickupIndex>();

	private static PickupIndex[] artifactIndexToPickupIndex = Array.Empty<PickupIndex>();

	private static PickupIndex[] miscPickupIndexToPickupIndex = Array.Empty<PickupIndex>();

	private static PickupIndex[] droneIndexToPickupIndex = Array.Empty<PickupIndex>();

	private static readonly Dictionary<string, PickupIndex> nameToPickupIndex = new Dictionary<string, PickupIndex>();

	private static readonly Dictionary<ItemTier, PickupIndex> itemTierToPickupIndex = new Dictionary<ItemTier, PickupIndex>();

	public static readonly Color invalidPickupColor = Color.black;

	public static readonly string invalidPickupToken = "???";

	public static ResourceAvailability availability = default(ResourceAvailability);

	public static Action<List<PickupDef>> modifyPickups;

	public static int pickupCount { get; private set; }

	public static GenericStaticEnumerable<PickupIndex, Enumerator> allPickupIndices => default(GenericStaticEnumerable<PickupIndex, Enumerator>);

	public static IEnumerable<PickupDef> allPickups => entries;

	[NotNull]
	public static T[] GetPerPickupBuffer<T>()
	{
		return new T[pickupCount];
	}

	public static void SetEntries([NotNull] PickupDef[] pickupDefs)
	{
		Array.Resize(ref entries, pickupDefs.Length);
		pickupCount = pickupDefs.Length;
		Array.Copy(pickupDefs, entries, entries.Length);
		Array.Resize(ref itemIndexToPickupIndex, ItemCatalog.itemCount);
		Array.Resize(ref equipmentIndexToPickupIndex, EquipmentCatalog.equipmentCount);
		Array.Resize(ref artifactIndexToPickupIndex, ArtifactCatalog.artifactCount);
		Array.Resize(ref miscPickupIndexToPickupIndex, MiscPickupCatalog.pickupCount);
		Array.Resize(ref droneIndexToPickupIndex, DroneCatalog.droneCount);
		nameToPickupIndex.Clear();
		itemTierToPickupIndex.Clear();
		for (int i = 0; i < entries.Length; i++)
		{
			PickupDef pickupDef = entries[i];
			PickupIndex pickupIndex = (pickupDef.pickupIndex = new PickupIndex(i));
			if (pickupDef.itemIndex != ItemIndex.None)
			{
				itemIndexToPickupIndex[(int)pickupDef.itemIndex] = pickupIndex;
			}
			else if (pickupDef.itemTier != ItemTier.NoTier)
			{
				itemTierToPickupIndex.Add(pickupDef.itemTier, pickupDef.pickupIndex);
			}
			if (pickupDef.equipmentIndex != EquipmentIndex.None)
			{
				equipmentIndexToPickupIndex[(int)pickupDef.equipmentIndex] = pickupIndex;
			}
			if (pickupDef.artifactIndex != ArtifactIndex.None)
			{
				artifactIndexToPickupIndex[(int)pickupDef.artifactIndex] = pickupIndex;
			}
			if (pickupDef.miscPickupIndex != MiscPickupIndex.None)
			{
				miscPickupIndexToPickupIndex[(int)pickupDef.miscPickupIndex] = pickupIndex;
			}
			if (pickupDef.droneIndex != DroneIndex.None)
			{
				droneIndexToPickupIndex[(int)pickupDef.droneIndex] = pickupIndex;
			}
		}
		for (int j = 0; j < entries.Length; j++)
		{
			PickupDef pickupDef2 = entries[j];
			nameToPickupIndex[pickupDef2.internalName] = pickupDef2.pickupIndex;
		}
		availability.MakeAvailable();
	}

	[SystemInitializer(new Type[]
	{
		typeof(ItemCatalog),
		typeof(EquipmentCatalog),
		typeof(ArtifactCatalog),
		typeof(MiscPickupCatalog)
	})]
	public static IEnumerator Init()
	{
		AsyncOperationHandle<GameObject> equipmentDefPreload = LegacyResourcesAPI.LoadAsync<GameObject>("Prefabs/ItemPickups/EquipmentOrb");
		while (!equipmentDefPreload.IsDone)
		{
			yield return null;
		}
		EquipmentDef.SetDropletDisplayPrefab(equipmentDefPreload.Result);
		List<PickupDef> pickupDefs = new List<PickupDef>();
		foreach (ItemTierDef allItemTierDef in ItemTierCatalog.allItemTierDefs)
		{
			PickupDef pickupDef = new PickupDef();
			pickupDef.internalName = "ItemTier." + allItemTierDef.tier;
			pickupDef.itemTier = allItemTierDef.tier;
			pickupDef.dropletDisplayPrefab = allItemTierDef?.dropletDisplayPrefab;
			pickupDef.baseColor = ColorCatalog.GetColor(allItemTierDef.colorIndex);
			pickupDef.darkColor = ColorCatalog.GetColor(allItemTierDef.darkColorIndex);
			pickupDef.interactContextToken = "ITEM_PICKUP_CONTEXT";
			pickupDef.isLunar = allItemTierDef.tier == ItemTier.Lunar;
			pickupDef.isBoss = allItemTierDef.tier == ItemTier.Boss;
			pickupDefs.Add(pickupDef);
		}
		yield return null;
		for (int i = 0; i < ItemCatalog.itemCount; i++)
		{
			PickupDef item = ItemCatalog.GetItemDef((ItemIndex)i).CreatePickupDef();
			pickupDefs.Add(item);
		}
		yield return null;
		for (int j = 0; j < EquipmentCatalog.equipmentCount; j++)
		{
			PickupDef item2 = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)j).CreatePickupDef();
			pickupDefs.Add(item2);
		}
		yield return null;
		for (int k = 0; k < MiscPickupCatalog.pickupCount; k++)
		{
			PickupDef item3 = MiscPickupCatalog.miscPickupDefs[k].CreatePickupDef();
			pickupDefs.Add(item3);
		}
		yield return null;
		for (int l = 0; l < ArtifactCatalog.artifactCount; l++)
		{
			PickupDef item4 = ArtifactCatalog.GetArtifactDef((ArtifactIndex)l).CreatePickupDef();
			pickupDefs.Add(item4);
		}
		yield return null;
		for (int m = 0; m < DroneCatalog.droneCount; m++)
		{
			PickupDef item5 = DroneCatalog.GetDroneDef((DroneIndex)m).CreatePickupDef();
			pickupDefs.Add(item5);
		}
		modifyPickups?.Invoke(pickupDefs);
		SetEntries(pickupDefs.ToArray());
	}

	public static PickupIndex FindScrapIndexForItemTier(ItemTier tier)
	{
		PickupIndex result = PickupIndex.none;
		switch (tier)
		{
		case ItemTier.Tier1:
			result = FindPickupIndex("ItemIndex.ScrapWhite");
			break;
		case ItemTier.Tier2:
			result = FindPickupIndex("ItemIndex.ScrapGreen");
			break;
		case ItemTier.Tier3:
			result = FindPickupIndex("ItemIndex.ScrapRed");
			break;
		case ItemTier.Boss:
			result = FindPickupIndex("ItemIndex.ScrapYellow");
			break;
		}
		return result;
	}

	public static PickupIndex FindPickupIndex([NotNull] string pickupName)
	{
		if (nameToPickupIndex.TryGetValue(pickupName, out var value))
		{
			return value;
		}
		return PickupIndex.none;
	}

	public static PickupIndex FindPickupIndex(ItemIndex itemIndex)
	{
		return ArrayUtils.GetSafe(itemIndexToPickupIndex, (int)itemIndex, in PickupIndex.none);
	}

	public static PickupIndex FindPickupIndex(ItemTier tier)
	{
		if (itemTierToPickupIndex.TryGetValue(tier, out var value))
		{
			return value;
		}
		return PickupIndex.none;
	}

	public static PickupIndex FindPickupIndex(EquipmentIndex equipmentIndex)
	{
		return ArrayUtils.GetSafe(equipmentIndexToPickupIndex, (int)equipmentIndex, in PickupIndex.none);
	}

	public static PickupIndex FindPickupIndex(ArtifactIndex artifactIndex)
	{
		return ArrayUtils.GetSafe(artifactIndexToPickupIndex, (int)artifactIndex, in PickupIndex.none);
	}

	public static PickupIndex FindPickupIndex(MiscPickupIndex miscIndex)
	{
		return ArrayUtils.GetSafe(miscPickupIndexToPickupIndex, (int)miscIndex, in PickupIndex.none);
	}

	public static PickupIndex FindPickupIndex(DroneIndex droneIndex)
	{
		return ArrayUtils.GetSafe(droneIndexToPickupIndex, (int)droneIndex, in PickupIndex.none);
	}

	[CanBeNull]
	public static PickupDef GetPickupDef(PickupIndex pickupIndex)
	{
		return ArrayUtils.GetSafe(entries, pickupIndex.value);
	}

	[NotNull]
	public static GameObject GetHiddenPickupDisplayPrefab()
	{
		return LegacyResourcesAPI.Load<GameObject>("Prefabs/PickupModels/PickupMystery");
	}

	[ConCommand(commandName = "pickup_print_all", flags = ConVarFlags.None, helpText = "Prints all pickup definitions.")]
	private static void CCPickupPrintAll(ConCommandArgs args)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < pickupCount; i++)
		{
			PickupDef pickupDef = GetPickupDef(new PickupIndex(i));
			stringBuilder.Append("[").Append(i).Append("]={internalName=")
				.Append(pickupDef.internalName)
				.Append("}")
				.AppendLine();
		}
	}
}
