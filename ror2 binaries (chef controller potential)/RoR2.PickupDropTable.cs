// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupDropTable
using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Items;
using UnityEngine;

public abstract class PickupDropTable : ScriptableObject
{
	public bool canDropBeReplaced = true;

	private static readonly List<PickupDropTable> instancesList;

	[Obsolete("Use GenerateDistinctPickupsFromWeightedSelection instead.", false)]
	protected static PickupIndex[] GenerateUniqueDropsFromWeightedSelection(int maxDrops, Xoroshiro128Plus rng, WeightedSelection<PickupIndex> weightedSelection)
	{
		Math.Min(maxDrops, weightedSelection.Count);
		List<PickupIndex> list = new List<PickupIndex>();
		GenerateDistinctFromWeightedSelection(list, maxDrops, rng, weightedSelection);
		return list.ToArray();
	}

	protected static List<T> GenerateDistinctFromWeightedSelection<T>(List<T> dest, int desiredCount, Xoroshiro128Plus rng, WeightedSelection<T> weightedSelection) where T : struct
	{
		int num = Math.Min(desiredCount, weightedSelection.Count);
		Span<int> span = ((num >= 16) ? ((Span<int>)new int[num]) : stackalloc int[num]);
		Span<int> span2 = span;
		for (int i = 0; i < num; i++)
		{
			int num2 = weightedSelection.EvaluateToChoiceIndex(rng.nextNormalizedFloat, span2.Slice(0, i));
			WeightedSelection<T>.ChoiceInfo choice = weightedSelection.GetChoice(num2);
			span2[i] = num2;
			dest.Add(choice.value);
		}
		return dest;
	}

	[Obsolete("Use GeneratePickupFromWeightedSelection instead.", false)]
	protected static PickupIndex GenerateDropFromWeightedSelection(Xoroshiro128Plus rng, WeightedSelection<PickupIndex> weightedSelection)
	{
		if (weightedSelection.Count > 0)
		{
			return weightedSelection.Evaluate(rng.nextNormalizedFloat);
		}
		return PickupIndex.none;
	}

	protected static UniquePickup GeneratePickupFromWeightedSelection(Xoroshiro128Plus rng, WeightedSelection<UniquePickup> weightedSelection)
	{
		if (weightedSelection.Count > 0)
		{
			return weightedSelection.Evaluate(rng.nextNormalizedFloat);
		}
		return UniquePickup.none;
	}

	public abstract int GetPickupCount();

	[Obsolete("Use GeneratePickupPreReplacement instead.", false)]
	protected virtual PickupIndex GenerateDropPreReplacement(Xoroshiro128Plus rng)
	{
		throw new NotImplementedException();
	}

	[Obsolete("Use GenerateDistinctPickupsPreReplacement instead.", false)]
	protected virtual PickupIndex[] GenerateUniqueDropsPreReplacement(int maxDrops, Xoroshiro128Plus rng)
	{
		throw new NotImplementedException();
	}

	protected virtual UniquePickup GeneratePickupPreReplacement(Xoroshiro128Plus rng)
	{
		return new UniquePickup
		{
			pickupIndex = CallGenerateDropPreReplacement(rng)
		};
		[Obsolete]
		PickupIndex CallGenerateDropPreReplacement(Xoroshiro128Plus rng2)
		{
			return GenerateDropPreReplacement(rng2);
		}
	}

	protected virtual void GenerateDistinctPickupsPreReplacement(List<UniquePickup> dest, int desiredCount, Xoroshiro128Plus rng)
	{
		PickupIndex[] array = CallGenerateUniqueDropsPreReplacement(desiredCount, rng);
		for (int i = 0; i < array.Length; i++)
		{
			dest[i] = new UniquePickup
			{
				pickupIndex = array[i]
			};
		}
		[Obsolete]
		PickupIndex[] CallGenerateUniqueDropsPreReplacement(int maxDrops, Xoroshiro128Plus rng2)
		{
			return GenerateUniqueDropsPreReplacement(maxDrops, rng2);
		}
	}

	public UniquePickup GeneratePickup(Xoroshiro128Plus rng)
	{
		UniquePickup uniquePickup = GeneratePickupPreReplacement(rng);
		if (uniquePickup.pickupIndex == PickupIndex.none)
		{
			Debug.LogError("Could not generate pickup from droptable.");
		}
		if (!uniquePickup.pickupIndex.isValid)
		{
			Debug.LogError($"Pickup index from droptable \"{this}\" is invalid. Replacing with none. Index={uniquePickup.pickupIndex}");
			uniquePickup = uniquePickup.WithPickupIndex(PickupIndex.none);
		}
		if (canDropBeReplaced)
		{
			uniquePickup = RandomlyLunarUtils.CheckForLunarReplacement(uniquePickup, rng);
		}
		return uniquePickup;
	}

	public void GenerateDistinctPickups(List<UniquePickup> dest, int desiredCount, Xoroshiro128Plus rng, bool allowLoop = true)
	{
		GenerateDistinctPickupsPreReplacement(dest, desiredCount, rng);
		if (allowLoop)
		{
			while (dest.Count < desiredCount)
			{
				int count = dest.Count;
				GenerateDistinctPickupsPreReplacement(dest, desiredCount - dest.Count, rng);
				if (dest.Count <= count)
				{
					break;
				}
			}
		}
		if (canDropBeReplaced)
		{
			RandomlyLunarUtils.CheckForLunarReplacementUniqueArray(dest, rng);
		}
	}

	[Obsolete("Use GeneratePickup instead.", false)]
	public PickupIndex GenerateDrop(Xoroshiro128Plus rng)
	{
		PickupIndex pickupIndex = GenerateDropPreReplacement(rng);
		if (pickupIndex == PickupIndex.none)
		{
			Debug.LogError("Could not generate pickup index from droptable.");
		}
		if (!pickupIndex.isValid)
		{
			Debug.LogError("Pickup index from droptable is invalid.");
		}
		if (canDropBeReplaced)
		{
			return RandomlyLunarUtils.CheckForLunarReplacement(pickupIndex, rng);
		}
		return pickupIndex;
	}

	[Obsolete("Use GenerateDistinctPickups instead.", false)]
	public PickupIndex[] GenerateUniqueDrops(int maxDrops, Xoroshiro128Plus rng)
	{
		PickupIndex[] array = GenerateUniqueDropsPreReplacement(maxDrops, rng);
		if (canDropBeReplaced)
		{
			RandomlyLunarUtils.CheckForLunarReplacementUniqueArray(array, rng);
		}
		return array;
	}

	protected virtual void Regenerate(Run run)
	{
	}

	protected virtual void OnEnable()
	{
		instancesList.Add(this);
		if ((bool)Run.instance)
		{
			Regenerate(Run.instance);
		}
	}

	protected virtual void OnDisable()
	{
		instancesList.Remove(this);
	}

	static PickupDropTable()
	{
		instancesList = new List<PickupDropTable>();
		Run.onRunStartGlobal += RegenerateAll;
		Run.onAvailablePickupsModified += RegenerateAll;
	}

	private static void RegenerateAll(Run run)
	{
		for (int i = 0; i < instancesList.Count; i++)
		{
			instancesList[i].Regenerate(run);
		}
	}

	public void ModifyTierWeights(float tier1, float tier2, float tier3)
	{
	}
}
