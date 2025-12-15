// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupIndex
using System;
using System.Collections;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public struct PickupIndex : IEquatable<PickupIndex>
{
	public struct Enumerator : IEnumerator<PickupIndex>, IEnumerator, IDisposable
	{
		private PickupIndex position;

		public readonly PickupIndex Current => position;

		readonly object IEnumerator.Current => Current;

		public bool MoveNext()
		{
			++position;
			return position.value < PickupCatalog.pickupCount;
		}

		public void Reset()
		{
			position = none;
		}

		void IDisposable.Dispose()
		{
		}
	}

	public static readonly PickupIndex none = new PickupIndex(-1);

	[SerializeField]
	public readonly int value;

	public readonly bool isValid => (uint)value < PickupCatalog.pickupCount;

	private readonly PickupDef pickupDef => PickupCatalog.GetPickupDef(this);

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly ItemIndex itemIndex => pickupDef?.itemIndex ?? ItemIndex.None;

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly EquipmentIndex equipmentIndex => pickupDef?.equipmentIndex ?? EquipmentIndex.None;

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly uint coinValue => pickupDef?.coinValue ?? 0;

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public static GenericStaticEnumerable<PickupIndex, Enumerator> allPickups => default(GenericStaticEnumerable<PickupIndex, Enumerator>);

	public PickupIndex(int value)
	{
		this.value = ((value < 0) ? (-1) : value);
	}

	public static bool operator ==(PickupIndex a, PickupIndex b)
	{
		return a.value == b.value;
	}

	public static bool operator !=(PickupIndex a, PickupIndex b)
	{
		return a.value != b.value;
	}

	public static bool operator <(PickupIndex a, PickupIndex b)
	{
		return a.value < b.value;
	}

	public static bool operator >(PickupIndex a, PickupIndex b)
	{
		return a.value > b.value;
	}

	public static bool operator <=(PickupIndex a, PickupIndex b)
	{
		return a.value >= b.value;
	}

	public static bool operator >=(PickupIndex a, PickupIndex b)
	{
		return a.value <= b.value;
	}

	public static PickupIndex operator ++(PickupIndex a)
	{
		return new PickupIndex(a.value + 1);
	}

	public static PickupIndex operator --(PickupIndex a)
	{
		return new PickupIndex(a.value - 1);
	}

	public override readonly bool Equals(object obj)
	{
		if (obj is PickupIndex)
		{
			return this == (PickupIndex)obj;
		}
		return false;
	}

	public readonly bool Equals(PickupIndex other)
	{
		return value == other.value;
	}

	public override readonly int GetHashCode()
	{
		return value.GetHashCode();
	}

	public static void WriteToNetworkWriter(NetworkWriter writer, PickupIndex value)
	{
		writer.WritePackedIndex32(value.value);
	}

	public static PickupIndex ReadFromNetworkReader(NetworkReader reader)
	{
		return new PickupIndex(reader.ReadPackedIndex32());
	}

	public override readonly string ToString()
	{
		return pickupDef?.internalName ?? $"BadPickupIndex{value}";
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public PickupIndex(ItemIndex itemIndex)
	{
		value = PickupCatalog.FindPickupIndex(itemIndex).value;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public PickupIndex(EquipmentIndex equipmentIndex)
	{
		value = PickupCatalog.FindPickupIndex(equipmentIndex).value;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly GameObject GetHiddenPickupDisplayPrefab()
	{
		return PickupCatalog.GetHiddenPickupDisplayPrefab();
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly GameObject GetPickupDisplayPrefab()
	{
		return pickupDef?.displayPrefab;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly GameObject GetPickupDropletDisplayPrefab()
	{
		return pickupDef?.dropletDisplayPrefab;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly Color GetPickupColor()
	{
		return pickupDef?.baseColor ?? Color.black;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly string GetPickupNameToken()
	{
		return pickupDef?.nameToken ?? "???";
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly UnlockableDef GetUnlockable()
	{
		return pickupDef?.unlockableDef;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly bool IsLunar()
	{
		return pickupDef?.isLunar ?? false;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public readonly bool IsBoss()
	{
		return pickupDef?.isBoss ?? false;
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public string GetInteractContextToken()
	{
		return pickupDef?.interactContextToken ?? "";
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public static PickupIndex Find(string name)
	{
		return PickupCatalog.FindPickupIndex(name);
	}

	[Obsolete("PickupIndex methods are deprecated. Use PickupCatalog instead.")]
	public static Enumerator GetEnumerator()
	{
		return default(Enumerator);
	}
}
