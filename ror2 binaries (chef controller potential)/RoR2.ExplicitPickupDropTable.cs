// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.ExplicitPickupDropTable
using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

[CreateAssetMenu(menuName = "RoR2/DropTables/ExplicitPickupDropTable")]
public class ExplicitPickupDropTable : PickupDropTable
{
	[Serializable]
	public struct StringEntry
	{
		public string pickupName;

		public float pickupWeight;
	}

	[Serializable]
	public struct PickupDefEntry
	{
		[TypeRestrictedReference(new Type[]
		{
			typeof(ItemDef),
			typeof(EquipmentDef),
			typeof(DroneDef),
			typeof(MiscPickupDef)
		})]
		public UnityEngine.Object pickupDef;

		public float pickupWeight;
	}

	public PickupDefEntry[] pickupEntries = Array.Empty<PickupDefEntry>();

	[Obsolete("Use pickupEntries instead.", false)]
	[Header("Deprecated")]
	public StringEntry[] entries = Array.Empty<StringEntry>();

	private readonly WeightedSelection<UniquePickup> weightedSelection = new WeightedSelection<UniquePickup>();

	protected override void Regenerate(Run run)
	{
		GenerateWeightedSelection();
	}

	private void GenerateWeightedSelection()
	{
		weightedSelection.Clear();
		StringEntry[] array = entries;
		for (int i = 0; i < array.Length; i++)
		{
			StringEntry stringEntry = array[i];
			weightedSelection.AddChoice(new UniquePickup(PickupCatalog.FindPickupIndex(stringEntry.pickupName)), stringEntry.pickupWeight);
		}
		PickupDefEntry[] array2 = pickupEntries;
		for (int i = 0; i < array2.Length; i++)
		{
			PickupDefEntry pickupDefEntry = array2[i];
			PickupIndex pickupIndex = PickupIndex.none;
			UnityEngine.Object pickupDef = pickupDefEntry.pickupDef;
			if (!(pickupDef is ItemDef itemDef))
			{
				if (!(pickupDef is EquipmentDef equipmentDef))
				{
					if (pickupDef is DroneDef droneDef)
					{
						pickupIndex = PickupCatalog.FindPickupIndex(droneDef.droneIndex);
					}
					else
					{
						MiscPickupDef miscPickupDef = pickupDefEntry.pickupDef as MiscPickupDef;
						if (miscPickupDef != null)
						{
							pickupIndex = PickupCatalog.FindPickupIndex(miscPickupDef.miscPickupIndex);
						}
					}
				}
				else
				{
					pickupIndex = PickupCatalog.FindPickupIndex(equipmentDef.equipmentIndex);
				}
			}
			else
			{
				pickupIndex = PickupCatalog.FindPickupIndex(itemDef.itemIndex);
			}
			if (pickupIndex != PickupIndex.none)
			{
				weightedSelection.AddChoice(new UniquePickup(pickupIndex), pickupDefEntry.pickupWeight);
			}
		}
	}

	protected override UniquePickup GeneratePickupPreReplacement(Xoroshiro128Plus rng)
	{
		return PickupDropTable.GeneratePickupFromWeightedSelection(rng, weightedSelection);
	}

	protected override void GenerateDistinctPickupsPreReplacement(List<UniquePickup> dest, int desiredCount, Xoroshiro128Plus rng)
	{
		PickupDropTable.GenerateDistinctFromWeightedSelection(dest, desiredCount, rng, weightedSelection);
	}

	public override int GetPickupCount()
	{
		return weightedSelection.Count;
	}
}
