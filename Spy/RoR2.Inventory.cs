// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.Inventory
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HG;
using JetBrains.Annotations;
using RoR2;
using RoR2.Networking;
using UnityEngine;
using UnityEngine.Networking;

public class Inventory : NetworkBehaviour
{
	private readonly ref struct InventoryChangeScope
	{
		private readonly Inventory inventory;

		public InventoryChangeScope(Inventory inventory)
		{
			this.inventory = inventory;
			inventory.inventoryChangeScopeCounter++;
		}

		public void Dispose()
		{
			inventory.inventoryChangeScopeCounter--;
			if (inventory.inventoryChangeScopeCounter == 0)
			{
				inventory.HandleInventoryChanged();
			}
		}
	}

	private interface IChangeItemCountImpl
	{
		bool silent { get; }

		bool allowNegative { get; }

		int GetStackCount(ItemIndex itemIndex);

		void Add(ItemIndex itemIndex, int count);
	}

	private struct GiveItemPermanentImpl : IChangeItemCountImpl
	{
		public Inventory inventory;

		public bool silent { get; set; }

		public bool allowNegative => false;

		public readonly int GetStackCount(ItemIndex itemIndex)
		{
			return inventory.permanentItemStacks.GetStackValue(itemIndex);
		}

		public void Add(ItemIndex itemIndex, int count)
		{
			inventory.permanentItemStacks.Add(itemIndex, count);
			inventory.SetDirtyBit(1u);
		}
	}

	private struct GiveItemChanneledImpl : IChangeItemCountImpl
	{
		public Inventory inventory;

		public bool silent => true;

		public bool allowNegative => true;

		public readonly int GetStackCount(ItemIndex itemIndex)
		{
			return inventory.channeledItemStacks.GetStackValue(itemIndex);
		}

		public void Add(ItemIndex itemIndex, int count)
		{
			inventory.channeledItemStacks.Add(itemIndex, count);
			inventory.SetDirtyBit(64u);
		}
	}

	public struct ItemStackValues
	{
		public float temporaryStacksValue;

		public int permanentStacks;

		public int totalStacks;

		public static ItemStackValues Create()
		{
			return default(ItemStackValues);
		}
	}

	public struct ItemAndStackValues
	{
		public ItemIndex itemIndex;

		public ItemStackValues stackValues;

		public static ItemAndStackValues Create()
		{
			return new ItemAndStackValues
			{
				itemIndex = ItemIndex.None,
				stackValues = ItemStackValues.Create()
			};
		}

		public readonly ItemAndStackValues WithItemIndex(ItemIndex itemIndex)
		{
			return new ItemAndStackValues
			{
				itemIndex = itemIndex,
				stackValues = stackValues
			};
		}

		public readonly int AddAsPickupsToList(List<UniquePickup> dest)
		{
			int count = dest.Count;
			PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(itemIndex);
			for (int i = 0; i < stackValues.permanentStacks; i++)
			{
				dest.Add(new UniquePickup
				{
					pickupIndex = pickupIndex
				});
			}
			int num = Mathf.FloorToInt(stackValues.temporaryStacksValue);
			float num2 = (float)((double)stackValues.temporaryStacksValue - (double)num);
			int j = 0;
			for (int num3 = num; j < num3; j++)
			{
				dest.Add(new UniquePickup
				{
					pickupIndex = pickupIndex,
					decayValue = 1f
				});
			}
			if (num2 > 0f)
			{
				dest.Add(new UniquePickup
				{
					pickupIndex = pickupIndex,
					decayValue = num2
				});
			}
			return dest.Count - count;
		}
	}

	public struct TryTransformRandomItemArgs
	{
		public struct FilterArgs
		{
			public ItemIndex itemIndex;

			public ItemStorageType itemStorageType;
		}

		public delegate ItemIndex FilterDelegate(FilterArgs args);

		public Xoroshiro128Plus rng;

		public FilterDelegate filter;

		public bool forbidPermanent;

		public bool forbidTemporary;
	}

	public struct TryTransformRandomItemsResult
	{
		public ItemIndex originalItemIndex;

		public ItemIndex newItemIndex;

		public ItemStorageType originalItemStorageType;
	}

	public struct ItemTransformation
	{
		public struct CanTakeResult
		{
			public Inventory inventory;

			public ItemAndStackValues takenItem;

			public ItemTransformationTypeIndex itemTransformationType;

			public TakeResult PerformTake()
			{
				using (new InventoryChangeScope(inventory))
				{
					if (takenItem.stackValues.permanentStacks > 0)
					{
						inventory.GiveItemPermanent(takenItem.itemIndex, -takenItem.stackValues.permanentStacks);
					}
					if (takenItem.stackValues.temporaryStacksValue > 0f)
					{
						inventory.GiveItemTemp(takenItem.itemIndex, 0f - takenItem.stackValues.temporaryStacksValue);
					}
				}
				return new TakeResult
				{
					inventory = inventory,
					takenItem = takenItem,
					transformationType = itemTransformationType
				};
			}
		}

		public struct TakeResult
		{
			public Inventory inventory;

			public ItemAndStackValues takenItem;

			public ItemTransformationTypeIndex transformationType;

			public TryTransformResult GiveTakenItem(Inventory inventory, ItemIndex newItemIndex)
			{
				TryTransformResult tryTransformResult = new TryTransformResult
				{
					inventory = inventory,
					takenItem = takenItem,
					givenItem = ((newItemIndex != ItemIndex.None) ? takenItem.WithItemIndex(newItemIndex) : ItemAndStackValues.Create()),
					transformationType = transformationType
				};
				using (new InventoryChangeScope(inventory))
				{
					if (takenItem.stackValues.temporaryStacksValue != 0f)
					{
						inventory.GiveItemTemp(newItemIndex, takenItem.stackValues.temporaryStacksValue);
					}
					if (takenItem.stackValues.permanentStacks != 0)
					{
						inventory.GiveItemPermanent(newItemIndex, takenItem.stackValues.permanentStacks);
					}
				}
				ItemTransformation.onItemTransformedServerGlobal?.Invoke(tryTransformResult);
				return tryTransformResult;
			}
		}

		public struct TryTransformResult
		{
			public Inventory inventory;

			public ItemAndStackValues takenItem;

			public ItemAndStackValues givenItem;

			public ItemTransformationTypeIndex transformationType;

			public readonly int totalTransformed => takenItem.stackValues.totalStacks;

			public static TryTransformResult Create()
			{
				return new TryTransformResult
				{
					inventory = null,
					takenItem = ItemAndStackValues.Create(),
					givenItem = ItemAndStackValues.Create()
				};
			}
		}

		private int? _minToTransform;

		private int? _maxToTransform;

		public ItemTransformationTypeIndex transformationType;

		public ItemIndex originalItemIndex { get; set; }

		public ItemIndex newItemIndex { get; set; }

		public bool allowWhenDisabled { get; set; }

		public int minToTransform
		{
			get
			{
				return _minToTransform ?? 1;
			}
			set
			{
				if (value <= 0)
				{
					throw new ArgumentOutOfRangeException("value", "Value must be a positive integer.");
				}
				_minToTransform = value;
			}
		}

		public int maxToTransform
		{
			get
			{
				return _maxToTransform ?? 1;
			}
			set
			{
				_maxToTransform = value;
			}
		}

		public bool forbidTempItems { get; set; }

		public bool forbidPermanentItems { get; set; }

		public static event Action<TryTransformResult> onItemTransformedServerGlobal;

		public bool TryTransform(Inventory inventory, out TryTransformResult result)
		{
			result = TryTransformResult.Create();
			if (TryTake(inventory, out var result2))
			{
				result.takenItem = result2.takenItem;
				result.givenItem = result2.GiveTakenItem(inventory, newItemIndex).givenItem;
				return true;
			}
			return false;
		}

		public bool CanTake(Inventory inventory, out CanTakeResult result)
		{
			result = new CanTakeResult
			{
				inventory = inventory,
				takenItem = new ItemAndStackValues
				{
					itemIndex = originalItemIndex
				},
				itemTransformationType = transformationType
			};
			if (inventory.inventoryDisabled && !allowWhenDisabled)
			{
				return false;
			}
			int toTake = ((!forbidTempItems) ? inventory.GetItemCountTemp(originalItemIndex) : 0);
			int toTake2 = ((!forbidPermanentItems) ? inventory.GetItemCountPermanent(originalItemIndex) : 0);
			int value = maxToTransform;
			int num = HGMath.TakeIntClamped(ref value, toTake);
			int permanentStacks = HGMath.TakeIntClamped(ref value, toTake2);
			int num2 = maxToTransform - value;
			if (num2 < minToTransform)
			{
				return false;
			}
			result.takenItem.stackValues.permanentStacks = permanentStacks;
			result.takenItem.stackValues.totalStacks = num2;
			if (num > 0)
			{
				float temporaryStacksValue = Mathf.Min(inventory.tempItemsStorage.GetItemRawValue(originalItemIndex), num);
				result.takenItem.stackValues.temporaryStacksValue = temporaryStacksValue;
			}
			return true;
		}

		public CanTakeResult? CanTake(Inventory inventory)
		{
			if (!CanTake(inventory, out var result))
			{
				return null;
			}
			return result;
		}

		public bool TryTake(Inventory inventory, out TakeResult result)
		{
			result = new TakeResult
			{
				inventory = inventory,
				takenItem = new ItemAndStackValues
				{
					itemIndex = originalItemIndex
				}
			};
			if (CanTake(inventory, out var result2))
			{
				result = result2.PerformTake();
				return true;
			}
			return false;
		}

		public TakeResult? TryTake(Inventory inventory)
		{
			if (!TryTake(inventory, out var result))
			{
				return null;
			}
			return result;
		}
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	private readonly struct RunFixedTimeStampNetSerializer : INetSerializer<Run.FixedTimeStamp>
	{
		public void Serialize(NetworkWriter writer, in Run.FixedTimeStamp value)
		{
			writer.Write(value);
		}

		public void Deserialize(NetworkReader reader, ref Run.FixedTimeStamp dest)
		{
			dest = reader.ReadFixedTimeStamp();
		}

		void INetSerializer<Run.FixedTimeStamp>.Serialize(NetworkWriter writer, in Run.FixedTimeStamp value)
		{
			Serialize(writer, in value);
		}
	}

	[StructLayout(LayoutKind.Sequential, Size = 1)]
	private readonly struct FixedTimeStampSparseListImpl : ISparseArrayImpl<Run.FixedTimeStamp>
	{
		public SparseIndex[] AllocDenseArray()
		{
			return ItemCatalog.PerItemBufferPool.Request<SparseIndex>();
		}

		public Run.FixedTimeStamp[] AllocSparseArray()
		{
			return ItemCatalog.PerItemBufferPool.Request<Run.FixedTimeStamp>();
		}

		public void FreeDenseArray(SparseIndex[] array)
		{
			ItemCatalog.PerItemBufferPool.Return(ref array);
		}

		public void FreeSparseArray(Run.FixedTimeStamp[] array)
		{
			ItemCatalog.PerItemBufferPool.Return(ref array);
		}
	}

	private struct TempItemsStorage
	{
		private struct GiveItemTempImpl : IChangeItemCountImpl
		{
			public Inventory inventory;

			public bool silent => true;

			public bool allowNegative => false;

			public readonly int GetStackCount(ItemIndex itemIndex)
			{
				return inventory.tempItemsStorage.tempItemStacks.GetStackValue(itemIndex);
			}

			public void Add(ItemIndex itemIndex, int count)
			{
				inventory.tempItemsStorage.tempItemStacks.Add(itemIndex, count);
				inventory.SetDirtyBit(32u);
			}
		}

		private readonly Inventory inventory;

		private SparseArrayStruct<Run.FixedTimeStamp, FixedTimeStampSparseListImpl> decayToZeroTimeStamps;

		private float decayDuration;

		private float invDecayDuration;

		private ItemCollection tempItemStacks;

		public TempItemsStorage(Inventory inventory)
		{
			this.inventory = inventory;
			decayToZeroTimeStamps = new SparseArrayStruct<Run.FixedTimeStamp, FixedTimeStampSparseListImpl>(default(FixedTimeStampSparseListImpl), Run.FixedTimeStamp.negativeInfinity);
			decayDuration = 1f;
			invDecayDuration = 1f;
			tempItemStacks = ItemCollection.Create();
		}

		public void Dispose()
		{
			tempItemStacks.Dispose();
			decayToZeroTimeStamps.Dispose();
		}

		public void SyncStacksToDecay()
		{
			for (int num = decayToZeroTimeStamps.nonDefaultIndicesCount - 1; num >= 0; num--)
			{
				ItemIndex valueIndex = (ItemIndex)decayToZeroTimeStamps.GetValueIndex((DenseIndex)num);
				SyncStackToDecay(valueIndex);
			}
		}

		private void SyncStackToDecay(ItemIndex itemIndex)
		{
			int stackValue = tempItemStacks.GetStackValue(itemIndex);
			int num = Mathf.CeilToInt(decayToZeroTimeStamps.GetValue((SparseIndex)itemIndex).timeUntilClamped * invDecayDuration);
			if (stackValue == num)
			{
				return;
			}
			if (NetworkServer.active)
			{
				if (num == 0)
				{
					decayToZeroTimeStamps.ResetValue((SparseIndex)itemIndex);
				}
				inventory.ChangeItemStacksCount(new GiveItemTempImpl
				{
					inventory = inventory
				}, itemIndex, num - stackValue);
			}
			else
			{
				tempItemStacks.SetStackValue(itemIndex, num);
				inventory.UpdateEffectiveItemStacks(itemIndex);
			}
		}

		public void SetDecayDurationServer(float newDecayDuration)
		{
			if (newDecayDuration <= 0f || float.IsInfinity(newDecayDuration) || float.IsNaN(newDecayDuration))
			{
				throw new ArgumentOutOfRangeException("newDecayDuration", string.Format("{0} must be a finite positive number. {1}={2}", "newDecayDuration", "newDecayDuration", newDecayDuration));
			}
			float num = decayDuration;
			decayDuration = newDecayDuration;
			invDecayDuration = 1f / decayDuration;
			if (num != 0f)
			{
				float num2 = newDecayDuration / num;
				int i = 0;
				for (int nonDefaultIndicesCount = decayToZeroTimeStamps.nonDefaultIndicesCount; i < nonDefaultIndicesCount; i++)
				{
					SparseIndex valueIndex = decayToZeroTimeStamps.GetValueIndex((DenseIndex)i);
					Run.FixedTimeStamp value = decayToZeroTimeStamps.GetValue(valueIndex);
					Run.FixedTimeStamp newValue = Run.FixedTimeStamp.now + value.timeUntil * num2;
					decayToZeroTimeStamps.SetValue(valueIndex, in newValue);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly float GetItemRawValue(ItemIndex index)
		{
			return decayToZeroTimeStamps.GetValue((SparseIndex)index).timeUntilClamped * invDecayDuration;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static float RawToStacksValue(float rawValue)
		{
			return Mathf.Ceil(rawValue);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static float RawToDecayValue(float rawValue)
		{
			return rawValue - Mathf.Floor(rawValue);
		}

		public readonly float GetItemDecayValue(ItemIndex index)
		{
			return RawToDecayValue(GetItemRawValue(index));
		}

		public readonly void WriteAllTempItemRawValues(Span<float> dest)
		{
			ReadOnlySpan<SparseIndex> nonDefaultIndicesSpan = decayToZeroTimeStamps.GetNonDefaultIndicesSpan();
			for (int i = 0; i < nonDefaultIndicesSpan.Length; i++)
			{
				SparseIndex sparseIndex = nonDefaultIndicesSpan[i];
				float num = decayToZeroTimeStamps.GetValue(sparseIndex).timeUntilClamped * invDecayDuration;
				dest[(int)sparseIndex] = num;
			}
		}

		public readonly void WriteAllTempItemDecayValues(Span<float> values)
		{
			ReadOnlySpan<SparseIndex> nonDefaultIndicesSpan = decayToZeroTimeStamps.GetNonDefaultIndicesSpan();
			for (int i = 0; i < nonDefaultIndicesSpan.Length; i++)
			{
				SparseIndex sparseIndex = nonDefaultIndicesSpan[i];
				float num = decayToZeroTimeStamps.GetValue(sparseIndex).timeUntilClamped * invDecayDuration;
				float num2 = num - Mathf.Floor(num);
				values[(int)sparseIndex] = num2;
			}
		}

		public readonly int GetTotalItemStacks()
		{
			return tempItemStacks.GetTotalItemStacks();
		}

		public readonly void Serialize(NetworkWriter writer)
		{
			writer.Write(decayDuration);
			decayToZeroTimeStamps.Serialize(writer, default(RunFixedTimeStampNetSerializer));
		}

		public void Deserialize(NetworkReader reader)
		{
			decayDuration = reader.ReadSingle();
			invDecayDuration = 1f / decayDuration;
			tempItemStacks.Clear();
			decayToZeroTimeStamps.Deserialize(reader, default(RunFixedTimeStampNetSerializer));
			SyncStacksToDecay();
		}

		public float GiveItemTemp(ItemIndex itemIndex, float decayValue)
		{
			if (!ItemCatalog.IsIndexValid(in itemIndex))
			{
				return 0f;
			}
			Run.FixedTimeStamp fixedTimeStamp = decayToZeroTimeStamps.GetValue((SparseIndex)itemIndex);
			if (fixedTimeStamp.Equals(decayToZeroTimeStamps.defaultValue))
			{
				fixedTimeStamp = Run.FixedTimeStamp.now;
			}
			decayValue -= Time.fixedDeltaTime * 0.5f * invDecayDuration;
			Run.FixedTimeStamp newValue = fixedTimeStamp + decayValue * decayDuration;
			if (newValue.hasPassed)
			{
				decayToZeroTimeStamps.ResetValue((SparseIndex)itemIndex);
			}
			else
			{
				decayToZeroTimeStamps.SetValue((SparseIndex)itemIndex, in newValue);
			}
			inventory.SetDirtyBit(32u);
			SyncStackToDecay(itemIndex);
			return (newValue.timeUntilClamped - fixedTimeStamp.timeUntilClamped) * invDecayDuration;
		}

		public void ResetItem(ItemIndex itemIndex)
		{
			if (ItemCatalog.IsIndexValid(in itemIndex))
			{
				decayToZeroTimeStamps.ResetValue((SparseIndex)itemIndex);
				inventory.SetDirtyBit(32u);
				SyncStackToDecay(itemIndex);
			}
		}

		public readonly int GetItemStacks(ItemIndex itemIndex)
		{
			return tempItemStacks.GetStackValue(itemIndex);
		}

		public readonly void GetNonZeroIndices(List<ItemIndex> dest)
		{
			tempItemStacks.GetNonZeroIndices(dest);
		}

		public readonly ReadOnlySpan<ItemIndex> GetNonZeroIndicesSpan()
		{
			return tempItemStacks.GetNonZeroIndicesSpan();
		}
	}

	private ItemCollection permanentItemStacks;

	private ItemCollection channeledItemStacks;

	private TempItemsStorage tempItemsStorage;

	private ItemCollection effectiveItemStacks;

	private int _lastExtraEquipmentCount;

	public readonly List<ItemIndex> itemAcquisitionOrder = new List<ItemIndex>();

	private bool[] itemAcquisitionSet;

	private const uint itemListDirtyBit = 1u;

	private const uint infusionBonusDirtyBit = 4u;

	private const uint itemAcquisitionOrderDirtyBit = 8u;

	private const uint equipmentDirtyBit = 16u;

	private const uint tempItemsDirtyBit = 32u;

	private const uint channeledItemListDirtyBit = 64u;

	private const uint disabledDirtyBit = 128u;

	private const uint allDirtyBits = 253u;

	public const float baseItemDecayDuration = 80f;

	private byte[] _activeEquipmentSet = Array.Empty<byte>();

	private bool _activeEquipmentDirty;

	public bool wasRecentlyExtraEquipmentSwapped;

	public bool wasRecentlyCreated;

	private EquipmentState[][] _equipmentStateSlots = Array.Empty<EquipmentState[]>();

	private bool equipmentDisabled;

	private int inventoryDisablerCountServer;

	private bool _inventoryDisabled;

	private int inventoryChangeScopeCounter;

	private static readonly Queue<Inventory> releaseQueue;

	[HideInInspector]
	public float beadAppliedHealth;

	[HideInInspector]
	public float beadAppliedShield;

	[HideInInspector]
	public float beadAppliedRegen;

	[HideInInspector]
	public float beadAppliedDamage;

	public static readonly Func<ItemIndex, bool> defaultItemCopyFilterDelegate;

	private static int kCmdCmdSwitchToNextEquipmentInSet;

	private static int kCmdCmdSwitchToPreviousEquipmentInSet;

	private static int kRpcRpcItemAdded;

	private static int kRpcRpcClientEquipmentChanged;

	[Obsolete("Do not use this; it will be removed in the future.", false)]
	private CharacterBody characterBody => GetComponent<CharacterMaster>().AsValidOrNull()?.GetBody();

	public EquipmentIndex currentEquipmentIndex => currentEquipmentState.equipmentIndex;

	public EquipmentState currentEquipmentState
	{
		get
		{
			if (activeEquipmentSet.Length == 0)
			{
				return EquipmentState.empty;
			}
			return GetEquipment(activeEquipmentSlot, activeEquipmentSet[activeEquipmentSlot]);
		}
	}

	public EquipmentIndex alternateEquipmentIndex => alternateEquipmentState.equipmentIndex;

	public EquipmentState alternateEquipmentState
	{
		get
		{
			int equipmentSetCount = GetEquipmentSetCount(activeEquipmentSlot);
			if (equipmentSetCount > 0)
			{
				int num = (activeEquipmentSet[activeEquipmentSlot] + 1) % equipmentSetCount;
				if (num != activeEquipmentSet[activeEquipmentSlot])
				{
					return GetEquipment(activeEquipmentSlot, (uint)num);
				}
			}
			return EquipmentState.empty;
		}
	}

	public byte activeEquipmentSlot { get; private set; }

	public byte[] activeEquipmentSet
	{
		get
		{
			return _activeEquipmentSet;
		}
		private set
		{
			_activeEquipmentSet = value;
		}
	}

	public bool inventoryDisabled
	{
		get
		{
			return _inventoryDisabled;
		}
		private set
		{
			if (_inventoryDisabled == value)
			{
				return;
			}
			using (new InventoryChangeScope(this))
			{
				_inventoryDisabled = value;
				UpdateAllEffectiveItemStacks();
				if (NetworkServer.active)
				{
					SetDirtyBit(128u);
				}
			}
		}
	}

	public uint infusionBonus { get; private set; }

	private bool spawnedOverNetwork => base.isServer;

	public event Action onInventoryChanged;

	public event Action onEquipmentExternalRestockServer;

	public static event Action<Inventory> onInventoryChangedGlobal;

	public static event Action<Inventory, ItemIndex, int> onServerItemGiven;

	public event Action<ItemIndex> onItemAddedClient;

	public event Action<EquipmentIndex, uint> onEquipmentChangedClient;

	public bool HasEquipmentOverflow()
	{
		int num = 0;
		for (int i = 0; i < GetEquipmentSetCount(activeEquipmentSlot); i++)
		{
			if (_equipmentStateSlots[activeEquipmentSlot][i].equipmentIndex != EquipmentIndex.None)
			{
				num++;
			}
		}
		return num > 2;
	}

	[Obsolete("For Mod Compat Only", false)]
	private bool SetEquipmentInternal(EquipmentState equipmentState, uint slot)
	{
		return SetEquipmentInternal(equipmentState, slot, FindBestEquipmentSetIndex(equipmentState.Equals(EquipmentState.empty)));
	}

	private bool SetEquipmentInternal(EquipmentState equipmentState, uint slot, uint set)
	{
		if (!Run.instance || Run.instance.IsEquipmentExpansionLocked(equipmentState.equipmentIndex))
		{
			return false;
		}
		if (_equipmentStateSlots.Length <= slot)
		{
			int num = _equipmentStateSlots.Length;
			Array.Resize(ref _equipmentStateSlots, (int)(slot + 1));
			Array.Resize(ref _activeEquipmentSet, (int)(slot + 1));
			for (int i = num; i < _equipmentStateSlots.Length; i++)
			{
				_equipmentStateSlots[i] = Array.Empty<EquipmentState>();
				_activeEquipmentSet[i] = 0;
			}
		}
		if (_equipmentStateSlots[slot].Length <= set)
		{
			int num2 = _equipmentStateSlots[slot].Length;
			Array.Resize(ref _equipmentStateSlots[slot], (int)(set + 1));
			for (int j = num2; j < _equipmentStateSlots[slot].Length; j++)
			{
				_equipmentStateSlots[slot][j] = EquipmentState.empty;
			}
		}
		if (_equipmentStateSlots[slot].Equals(equipmentState))
		{
			return false;
		}
		_equipmentStateSlots[slot][set] = equipmentState;
		return true;
	}

	public bool EquipmentSetFull()
	{
		if (_equipmentStateSlots.Length <= activeEquipmentSlot || _equipmentStateSlots[activeEquipmentSlot].Length == 0)
		{
			return false;
		}
		for (int i = 0; i < _equipmentStateSlots[activeEquipmentSlot].Length; i++)
		{
			if (_equipmentStateSlots[activeEquipmentSlot][i].equipmentIndex == EquipmentIndex.None)
			{
				return false;
			}
		}
		return true;
	}

	public void DispatchSwitchToNextEquipmentInSet()
	{
		wasRecentlyExtraEquipmentSwapped = true;
		if (NetworkServer.active)
		{
			SwitchToNextEquipmentInSet();
		}
		else
		{
			CallCmdSwitchToNextEquipmentInSet();
		}
	}

	[Command]
	public void CmdSwitchToNextEquipmentInSet()
	{
		SwitchToNextEquipmentInSet();
	}

	[Server]
	public void SwitchToNextEquipmentInSet()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SwitchToNextEquipmentInSet()' called on client");
		}
		else if (activeEquipmentSet.Length > activeEquipmentSlot)
		{
			activeEquipmentSet[activeEquipmentSlot] = (byte)((activeEquipmentSet[activeEquipmentSlot] + 1) % _equipmentStateSlots[activeEquipmentSlot].Length);
			_activeEquipmentDirty = true;
			wasRecentlyExtraEquipmentSwapped = true;
			SetDirtyBit(16u);
			HandleInventoryChanged();
		}
	}

	[Command]
	public void CmdSwitchToPreviousEquipmentInSet()
	{
		SwitchToPreviousEquipmentInSet();
	}

	[Server]
	public void SwitchToPreviousEquipmentInSet()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SwitchToPreviousEquipmentInSet()' called on client");
		}
		else if (activeEquipmentSet.Length > activeEquipmentSlot)
		{
			activeEquipmentSet[activeEquipmentSlot] = (byte)((activeEquipmentSet[activeEquipmentSlot] - 1 + _equipmentStateSlots[activeEquipmentSlot].Length) % _equipmentStateSlots[activeEquipmentSlot].Length);
			_activeEquipmentDirty = true;
			wasRecentlyExtraEquipmentSwapped = true;
			SetDirtyBit(16u);
			HandleInventoryChanged();
		}
	}

	public EquipmentIndex GetReplacedEquipmentIndex()
	{
		uint num = FindBestEquipmentSetIndex();
		if (_equipmentStateSlots.Length > activeEquipmentSlot && _equipmentStateSlots[activeEquipmentSlot].Length > num)
		{
			return _equipmentStateSlots[activeEquipmentSlot][num].equipmentIndex;
		}
		return EquipmentIndex.None;
	}

	[Server]
	[Obsolete("For Mod Compat Only. Use \"SetEquipment(EquipmentState equipmentState, uint slot, uint set)\" instead.", false)]
	public void SetEquipment(EquipmentState equipmentState, uint slot)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipment(RoR2.EquipmentState,System.UInt32)' called on client");
		}
		else
		{
			SetEquipment(equipmentState, slot, (byte)FindBestEquipmentSetIndex(equipmentState.Equals(EquipmentState.empty)));
		}
	}

	[Server]
	public void SetEquipment(EquipmentState equipmentState, uint slot, uint set)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipment(RoR2.EquipmentState,System.UInt32,System.UInt32)' called on client");
		}
		else if (SetEquipmentInternal(equipmentState, slot, set))
		{
			if (NetworkServer.active)
			{
				SetDirtyBit(16u);
			}
			HandleInventoryChanged();
			if (spawnedOverNetwork)
			{
				CallRpcClientEquipmentChanged(equipmentState.equipmentIndex, slot);
			}
		}
	}

	public EquipmentState GetActiveEquipment()
	{
		if (activeEquipmentSlot >= _equipmentStateSlots.Length || activeEquipmentSet.Length <= activeEquipmentSlot)
		{
			return EquipmentState.empty;
		}
		return _equipmentStateSlots[activeEquipmentSlot][activeEquipmentSet[activeEquipmentSlot]];
	}

	[Obsolete("For Mod Compat Only. Use \"GetEquipment(uint slot, uint set)\" instead.", false)]
	public EquipmentState GetEquipment(uint slot)
	{
		return GetEquipment(slot, FindBestEquipmentSetIndex(findExisting: true));
	}

	public EquipmentState GetEquipment(uint slot, uint set)
	{
		if (slot >= _equipmentStateSlots.Length || set >= _equipmentStateSlots[slot].Length)
		{
			return EquipmentState.empty;
		}
		return _equipmentStateSlots[slot][set];
	}

	[Server]
	public void SetActiveEquipmentSlot(byte slotIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetActiveEquipmentSlot(System.Byte)' called on client");
			return;
		}
		if (GetEquipmentSlotCount() <= slotIndex)
		{
			SetEquipmentInternal(EquipmentState.empty, slotIndex, (uint)GetEquipmentSetCount(activeEquipmentSlot));
		}
		activeEquipmentSlot = slotIndex;
		_activeEquipmentDirty = true;
		SetDirtyBit(16u);
		HandleInventoryChanged();
	}

	public void SetActiveEquipmentSet(byte setIndex)
	{
		if (GetEquipmentSetCount(activeEquipmentSlot) <= setIndex)
		{
			SetEquipmentInternal(EquipmentState.empty, activeEquipmentSlot, setIndex);
		}
		activeEquipmentSet[activeEquipmentSlot] = setIndex;
		_activeEquipmentDirty = true;
		SetDirtyBit(16u);
		HandleInventoryChanged();
	}

	public void AddEquipmentSet()
	{
		if (_equipmentStateSlots.Length == 0)
		{
			SetEquipment(EquipmentState.empty, 0u, 0u);
		}
		int num = Math.Max(GetEquipmentSlotCount(), 1);
		for (int i = 0; i < num; i++)
		{
			if (_equipmentStateSlots[i].Length == 0)
			{
				SetEquipment(EquipmentState.empty, (uint)i, 0u);
			}
			SetEquipment(EquipmentState.empty, (uint)i, (uint)_equipmentStateSlots[i].Length);
		}
	}

	public void RemoveEquipmentSet()
	{
		for (int i = 0; i < GetEquipmentSlotCount(); i++)
		{
			if ((bool)characterBody && _equipmentStateSlots[i][_equipmentStateSlots[i].Length - 1].equipmentIndex != EquipmentIndex.None)
			{
				PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(_equipmentStateSlots[i][_equipmentStateSlots[i].Length - 1].equipmentIndex), characterBody.transform.position, new Vector3(UnityEngine.Random.Range(-4f, 4f), 20f, UnityEngine.Random.Range(-4f, 4f)));
			}
			Array.Resize(ref _equipmentStateSlots[i], _equipmentStateSlots[i].Length - 1);
			activeEquipmentSet[i] = Math.Min(activeEquipmentSet[i], (byte)(_equipmentStateSlots[i].Length - 1));
		}
	}

	[Server]
	public void SetEquipmentDisabled(bool _active)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipmentDisabled(System.Boolean)' called on client");
			return;
		}
		equipmentDisabled = _active;
		SetDirtyBit(16u);
		HandleInventoryChanged();
	}

	public bool GetEquipmentDisabled()
	{
		return equipmentDisabled;
	}

	public int GetEquipmentSlotCount()
	{
		return _equipmentStateSlots.Length;
	}

	public int GetEquipmentSetCount(uint slot)
	{
		if (_equipmentStateSlots.Length <= slot)
		{
			return 0;
		}
		return _equipmentStateSlots[slot].Length;
	}

	public uint FindBestEquipmentSetIndex(bool findExisting = false)
	{
		if (activeEquipmentSet.Length <= activeEquipmentSlot || _equipmentStateSlots.Length <= activeEquipmentSlot || _equipmentStateSlots[activeEquipmentSlot].Length == 0)
		{
			return 0u;
		}
		if (!findExisting)
		{
			for (uint num = 0u; num < _equipmentStateSlots[activeEquipmentSlot].Length; num++)
			{
				if (_equipmentStateSlots[activeEquipmentSlot][num].equipmentIndex == EquipmentIndex.None)
				{
					return num;
				}
			}
		}
		return activeEquipmentSet[activeEquipmentSlot];
	}

	[Obsolete("Utilize \"SetEquipmentIndex(EquipmentIndex newEquipmentIndex, bool isRemovingEquipment)\" instead.")]
	[Server]
	public void SetEquipmentIndex(EquipmentIndex newEquipmentIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipmentIndex(RoR2.EquipmentIndex)' called on client");
		}
		else
		{
			SetEquipmentIndex(newEquipmentIndex, newEquipmentIndex == EquipmentIndex.None);
		}
	}

	[Server]
	public void SetEquipmentIndex(EquipmentIndex newEquipmentIndex, bool isRemovingEquipment)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipmentIndex(RoR2.EquipmentIndex,System.Boolean)' called on client");
			return;
		}
		uint set = FindBestEquipmentSetIndex(isRemovingEquipment);
		SetEquipmentIndexForSlot(newEquipmentIndex, activeEquipmentSlot, set);
	}

	[Server]
	[Obsolete("For Mod Compat Only. Use \"SetEquipmentIndexForSlot(EquipmentIndex newEquipmentIndex, uint slot, uint set)\" instead.", false)]
	public void SetEquipmentIndexForSlot(EquipmentIndex newEquipmentIndex, uint slot)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipmentIndexForSlot(RoR2.EquipmentIndex,System.UInt32)' called on client");
		}
		else
		{
			SetEquipmentIndexForSlot(newEquipmentIndex, slot, FindBestEquipmentSetIndex(newEquipmentIndex == EquipmentIndex.None));
		}
	}

	[Server]
	public void SetEquipmentIndexForSlot(EquipmentIndex newEquipmentIndex, uint slot, uint set)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetEquipmentIndexForSlot(RoR2.EquipmentIndex,System.UInt32,System.UInt32)' called on client");
		}
		else
		{
			if (Run.instance.IsEquipmentExpansionLocked(newEquipmentIndex))
			{
				return;
			}
			EquipmentState equipment = GetEquipment(slot, set);
			if (equipment.equipmentIndex != newEquipmentIndex)
			{
				byte charges = equipment.charges;
				Run.FixedTimeStamp chargeFinishTime = equipment.chargeFinishTime;
				if (equipment.equipmentIndex == EquipmentIndex.None && chargeFinishTime.isNegativeInfinity)
				{
					charges = 1;
					chargeFinishTime = Run.FixedTimeStamp.now;
				}
				EquipmentState equipmentState = new EquipmentState(newEquipmentIndex, chargeFinishTime, charges);
				SetEquipment(equipmentState, slot, set);
			}
		}
	}

	public EquipmentIndex GetEquipmentIndex()
	{
		return currentEquipmentIndex;
	}

	[Server]
	[Obsolete("For Mod Compat Only. Use \"DeductEquipmentCharges(byte slot, byte set, int deduction)\" instead.", false)]
	public void DeductEquipmentCharges(byte slot, int deduction)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::DeductEquipmentCharges(System.Byte,System.Int32)' called on client");
		}
		else
		{
			DeductEquipmentCharges(slot, (byte)FindBestEquipmentSetIndex(findExisting: true), deduction);
		}
	}

	[Server]
	public void DeductEquipmentCharges(byte slot, byte set, int deduction)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::DeductEquipmentCharges(System.Byte,System.Byte,System.Int32)' called on client");
			return;
		}
		EquipmentState equipment = GetEquipment(slot, set);
		byte charges = equipment.charges;
		Run.FixedTimeStamp chargeFinishTime = equipment.chargeFinishTime;
		charges = (byte)((charges >= deduction) ? ((byte)(charges - (byte)deduction)) : 0);
		SetEquipment(new EquipmentState(equipment.equipmentIndex, chargeFinishTime, charges), slot, set);
		UpdateEquipment();
	}

	public int GetActiveEquipmentRestockableChargeCount()
	{
		EquipmentState activeEquipment = GetActiveEquipment();
		if (activeEquipment.equipmentIndex == EquipmentIndex.None)
		{
			return 0;
		}
		return HGMath.ByteSafeSubtract((byte)GetEquipmentSlotMaxCharges(), activeEquipment.charges);
	}

	[Obsolete("For Mod Compat Only. Use \"GetEquipmentRestockableChargeCount(byte slot, byte set)\" instead.", false)]
	public int GetEquipmentRestockableChargeCount(byte slot)
	{
		return GetEquipmentRestockableChargeCount(slot, (byte)FindBestEquipmentSetIndex(findExisting: true));
	}

	public int GetEquipmentRestockableChargeCount(byte slot, byte set)
	{
		EquipmentState equipment = GetEquipment(slot, set);
		if (equipment.equipmentIndex == EquipmentIndex.None)
		{
			return 0;
		}
		return HGMath.ByteSafeSubtract((byte)GetEquipmentSlotMaxCharges(), equipment.charges);
	}

	[Server]
	public void RestockActiveEquipmentCharges(int amount)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RestockActiveEquipmentCharges(System.Int32)' called on client");
		}
		else if (GetActiveEquipment().equipmentIndex != EquipmentIndex.None)
		{
			RestockEquipmentCharges(activeEquipmentSlot, activeEquipmentSet[activeEquipmentSlot], amount);
		}
	}

	[Obsolete("For Mod Compat Only. Use \"RestockEquipmentCharges(byte slot, byte set, int amount)\" instead.", false)]
	[Server]
	public void RestockEquipmentCharges(byte slot, int amount)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RestockEquipmentCharges(System.Byte,System.Int32)' called on client");
		}
		else
		{
			RestockEquipmentCharges(slot, (byte)FindBestEquipmentSetIndex(findExisting: true), amount);
		}
	}

	[Server]
	public void RestockEquipmentCharges(byte slot, byte set, int amount)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RestockEquipmentCharges(System.Byte,System.Byte,System.Int32)' called on client");
			return;
		}
		amount = Math.Min(amount, GetEquipmentRestockableChargeCount(slot, set));
		if (amount > 0)
		{
			EquipmentState equipment = GetEquipment(slot, set);
			byte charges = HGMath.ByteSafeAdd(equipment.charges, (byte)Math.Min(amount, 255));
			SetEquipment(new EquipmentState(equipment.equipmentIndex, equipment.chargeFinishTime, charges), slot, set);
			this.onEquipmentExternalRestockServer?.Invoke();
		}
	}

	[Server]
	public void DeductActiveEquipmentCooldown(float seconds)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::DeductActiveEquipmentCooldown(System.Single)' called on client");
		}
		else if (activeEquipmentSet.Length != 0)
		{
			EquipmentState equipment = GetEquipment(activeEquipmentSlot, activeEquipmentSet[activeEquipmentSlot]);
			SetEquipment(new EquipmentState(equipment.equipmentIndex, equipment.chargeFinishTime - seconds, equipment.charges), activeEquipmentSlot, activeEquipmentSet[activeEquipmentSlot]);
		}
	}

	[Server]
	[Obsolete("For Mod Compat Only. Use \"DeductEquipmentCooldown(byte slot, byte set, float percentage)\" instead.", false)]
	public void DeductEquipmentCooldown(byte slot, float percentage)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::DeductEquipmentCooldown(System.Byte,System.Single)' called on client");
		}
		else
		{
			DeductEquipmentCooldown(slot, (byte)FindBestEquipmentSetIndex(findExisting: true), percentage);
		}
	}

	[Server]
	public void DeductEquipmentCooldown(byte slot, byte set, float percentage)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::DeductEquipmentCooldown(System.Byte,System.Byte,System.Single)' called on client");
			return;
		}
		EquipmentState equipment = GetEquipment(slot, set);
		Run.FixedTimeStamp chargeFinishTime = equipment.chargeFinishTime - equipment.equipmentDef.cooldown * percentage;
		SetEquipment(new EquipmentState(equipment.equipmentIndex, chargeFinishTime, equipment.charges), slot, set);
	}

	[Obsolete("For Mod Compat Only. Use \"GetEquipmentSlotMaxCharges()\" instead.", false)]
	public int GetEquipmentSlotMaxCharges(byte slot)
	{
		return GetEquipmentSlotMaxCharges();
	}

	public int GetEquipmentSlotMaxCharges()
	{
		return Math.Min(1 + GetItemCountEffective(RoR2Content.Items.EquipmentMagazine), 255);
	}

	public int GetActiveEquipmentMaxCharges()
	{
		return GetEquipmentSlotMaxCharges();
	}

	[Server]
	private void UpdateEquipment()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::UpdateEquipment()' called on client");
			return;
		}
		Run.FixedTimeStamp now = Run.FixedTimeStamp.now;
		byte b = (byte)Mathf.Min(1 + GetItemCountEffective(RoR2Content.Items.EquipmentMagazine), 255);
		for (uint num = 0u; num < _equipmentStateSlots.Length; num++)
		{
			for (uint num2 = 0u; num2 < _equipmentStateSlots[num].Length; num2++)
			{
				EquipmentState equipmentState = _equipmentStateSlots[num][num2];
				if (equipmentState.equipmentIndex == EquipmentIndex.None)
				{
					continue;
				}
				if (equipmentState.charges < b)
				{
					Run.FixedTimeStamp fixedTimeStamp = equipmentState.chargeFinishTime;
					byte b2 = equipmentState.charges;
					if (!fixedTimeStamp.isPositiveInfinity)
					{
						b2++;
					}
					if (fixedTimeStamp.isInfinity)
					{
						fixedTimeStamp = now;
					}
					if (fixedTimeStamp.hasPassed)
					{
						float num3 = equipmentState.equipmentDef.cooldown * CalculateEquipmentCooldownScale();
						SetEquipment(new EquipmentState(equipmentState.equipmentIndex, fixedTimeStamp + num3, b2), num, num2);
					}
				}
				if (equipmentState.charges >= b && !equipmentState.chargeFinishTime.isPositiveInfinity)
				{
					SetEquipment(new EquipmentState(equipmentState.equipmentIndex, Run.FixedTimeStamp.positiveInfinity, b), num, num2);
				}
			}
		}
		wasRecentlyExtraEquipmentSwapped = false;
	}

	private float CalculateEquipmentCooldownScale()
	{
		int itemCountEffective = GetItemCountEffective(RoR2Content.Items.EquipmentMagazine);
		int itemCountEffective2 = GetItemCountEffective(RoR2Content.Items.AutoCastEquipment);
		int itemCountEffective3 = GetItemCountEffective(RoR2Content.Items.BoostEquipmentRecharge);
		float num = Mathf.Pow(0.85f, itemCountEffective);
		if (itemCountEffective2 > 0)
		{
			num *= 0.5f * Mathf.Pow(0.85f, itemCountEffective2 - 1);
		}
		if (itemCountEffective3 > 0)
		{
			num *= Mathf.Pow(0.9f, itemCountEffective3);
		}
		return num;
	}

	[Server]
	public void AddInventoryDisabler()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddInventoryDisabler()' called on client");
		}
		else if (inventoryDisablerCountServer++ == 0)
		{
			inventoryDisabled = true;
		}
	}

	[Server]
	public void RemoveInventoryDisabler()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveInventoryDisabler()' called on client");
		}
		else if (--inventoryDisablerCountServer == 0)
		{
			inventoryDisabled = false;
		}
	}

	private void HandleInventoryChanged()
	{
		this.onInventoryChanged?.Invoke();
		Inventory.onInventoryChangedGlobal?.Invoke(this);
	}

	private void OnEnable()
	{
		SceneExitController.onBeginExit += OnSceneExiting;
	}

	private void OnDisable()
	{
		SceneExitController.onBeginExit -= OnSceneExiting;
	}

	private void Awake()
	{
		AcquirePooledResources();
	}

	private void OnDestroy()
	{
		releaseQueue.Enqueue(this);
	}

	private void FixedUpdate()
	{
		MyFixedUpdate(Time.deltaTime);
	}

	static Inventory()
	{
		releaseQueue = new Queue<Inventory>();
		defaultItemCopyFilterDelegate = DefaultItemCopyFilter;
		RoR2Application.onFixedUpdate += StaticFixedUpdate;
		kCmdCmdSwitchToNextEquipmentInSet = -1262252883;
		NetworkBehaviour.RegisterCommandDelegate(typeof(Inventory), kCmdCmdSwitchToNextEquipmentInSet, InvokeCmdCmdSwitchToNextEquipmentInSet);
		kCmdCmdSwitchToPreviousEquipmentInSet = 1681590321;
		NetworkBehaviour.RegisterCommandDelegate(typeof(Inventory), kCmdCmdSwitchToPreviousEquipmentInSet, InvokeCmdCmdSwitchToPreviousEquipmentInSet);
		kRpcRpcItemAdded = 1978705787;
		NetworkBehaviour.RegisterRpcDelegate(typeof(Inventory), kRpcRpcItemAdded, InvokeRpcRpcItemAdded);
		kRpcRpcClientEquipmentChanged = 1435548707;
		NetworkBehaviour.RegisterRpcDelegate(typeof(Inventory), kRpcRpcClientEquipmentChanged, InvokeRpcRpcClientEquipmentChanged);
		NetworkCRC.RegisterBehaviour("Inventory", 0);
	}

	private static void StaticFixedUpdate()
	{
		while (releaseQueue.Count > 0)
		{
			releaseQueue.Dequeue().ReleasePooledResources();
		}
	}

	private void AcquirePooledResources()
	{
		permanentItemStacks = ItemCollection.Create();
		tempItemsStorage = new TempItemsStorage(this);
		channeledItemStacks = ItemCollection.Create();
		effectiveItemStacks = ItemCollection.Create();
		if (NetworkServer.active)
		{
			itemAcquisitionSet = ItemCatalog.PerItemBufferPool.Request<bool>();
		}
		if (NetworkServer.active)
		{
			tempItemsStorage.SetDecayDurationServer(80f);
		}
	}

	private void ReleasePooledResources()
	{
		if (itemAcquisitionSet != null)
		{
			ItemCatalog.PerItemBufferPool.Return(ref itemAcquisitionSet);
		}
		effectiveItemStacks.Dispose();
		channeledItemStacks.Dispose();
		tempItemsStorage.Dispose();
		permanentItemStacks.Dispose();
	}

	private void MyFixedUpdate(float deltaTime)
	{
		if (NetworkServer.active)
		{
			RefreshInventoryStateServer();
		}
		tempItemsStorage.SyncStacksToDecay();
	}

	private void LateUpdate()
	{
		wasRecentlyExtraEquipmentSwapped = false;
	}

	public void RefreshInventoryStateServer()
	{
		UpdateEquipmentSetCount();
		UpdateEquipment();
	}

	private void UpdateEquipmentSetCount()
	{
		int num = CalculateEffectiveItemStacks(DLC3Content.Items.ExtraEquipment.itemIndex);
		int i = num - _lastExtraEquipmentCount;
		_lastExtraEquipmentCount = num;
		while (i > 0)
		{
			AddEquipmentSet();
			i--;
		}
		for (; i < 0; i++)
		{
			RemoveEquipmentSet();
		}
	}

	public void OnSceneExiting(SceneExitController exitController)
	{
	}

	[Server]
	public void AddInfusionBonus(uint value)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddInfusionBonus(System.UInt32)' called on client");
		}
		else if (value != 0)
		{
			infusionBonus += value;
			SetDirtyBit(4u);
			HandleInventoryChanged();
		}
	}

	[Server]
	public void GiveItemString(string itemString)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemString(System.String)' called on client");
		}
		else
		{
			GiveItemPermanent(ItemCatalog.FindItemIndex(itemString));
		}
	}

	[Server]
	public void GiveItemString(string itemString, int count)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemString(System.String,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(ItemCatalog.FindItemIndex(itemString), count);
		}
	}

	[Server]
	public void GiveEquipmentString(string equipmentString)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveEquipmentString(System.String)' called on client");
		}
		else
		{
			SetEquipmentIndex(EquipmentCatalog.FindEquipmentIndex(equipmentString));
		}
	}

	[Server]
	public void GiveRandomItems(int count, bool lunarEnabled, bool voidEnabled)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveRandomItems(System.Int32,System.Boolean,System.Boolean)' called on client");
			return;
		}
		try
		{
			if (count > 0)
			{
				WeightedSelection<List<PickupIndex>> weightedSelection = new WeightedSelection<List<PickupIndex>>();
				weightedSelection.AddChoice(Run.instance.availableTier1DropList, 100f);
				weightedSelection.AddChoice(Run.instance.availableTier2DropList, 60f);
				weightedSelection.AddChoice(Run.instance.availableTier3DropList, 4f);
				if (lunarEnabled)
				{
					weightedSelection.AddChoice(Run.instance.availableLunarItemDropList, 4f);
				}
				if (voidEnabled)
				{
					weightedSelection.AddChoice(Run.instance.availableVoidTier1DropList, 4f);
					weightedSelection.AddChoice(Run.instance.availableVoidTier2DropList, 2.3999999f);
					weightedSelection.AddChoice(Run.instance.availableVoidTier3DropList, 0.16f);
				}
				for (int i = 0; i < count; i++)
				{
					List<PickupIndex> list = weightedSelection.Evaluate(UnityEngine.Random.value);
					GiveItemPermanent(PickupCatalog.GetPickupDef(list[UnityEngine.Random.Range(0, list.Count)])?.itemIndex ?? ItemIndex.None);
				}
			}
		}
		catch (ArgumentException)
		{
		}
	}

	[Server]
	public void GiveRandomItems(int count, params ItemTier[] tiers)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveRandomItems(System.Int32,RoR2.ItemTier[])' called on client");
			return;
		}
		try
		{
			if (count <= 0)
			{
				return;
			}
			WeightedSelection<List<PickupIndex>> weightedSelection = new WeightedSelection<List<PickupIndex>>();
			for (int i = 0; i < tiers.Length; i++)
			{
				switch (tiers[i])
				{
				case ItemTier.Tier1:
					weightedSelection.AddChoice(Run.instance.availableTier1DropList, 100f);
					break;
				case ItemTier.Tier2:
					weightedSelection.AddChoice(Run.instance.availableTier2DropList, 60f);
					break;
				case ItemTier.Tier3:
					weightedSelection.AddChoice(Run.instance.availableTier3DropList, 4f);
					break;
				case ItemTier.Lunar:
					weightedSelection.AddChoice(Run.instance.availableLunarItemDropList, 4f);
					break;
				case ItemTier.Boss:
					weightedSelection.AddChoice(Run.instance.availableBossDropList, 1f);
					break;
				case ItemTier.VoidTier1:
					weightedSelection.AddChoice(Run.instance.availableVoidTier1DropList, 4f);
					break;
				case ItemTier.VoidTier2:
					weightedSelection.AddChoice(Run.instance.availableVoidTier2DropList, 2.3999999f);
					break;
				case ItemTier.VoidTier3:
					weightedSelection.AddChoice(Run.instance.availableVoidTier3DropList, 0.16f);
					break;
				case ItemTier.VoidBoss:
					weightedSelection.AddChoice(Run.instance.availableVoidBossDropList, 0.04f);
					break;
				}
			}
			if (weightedSelection.Count > 0)
			{
				for (int j = 0; j < count; j++)
				{
					List<PickupIndex> list = weightedSelection.Evaluate(UnityEngine.Random.value);
					GiveItemPermanent(PickupCatalog.GetPickupDef(list[UnityEngine.Random.Range(0, list.Count)])?.itemIndex ?? ItemIndex.None);
				}
			}
		}
		catch (ArgumentException)
		{
		}
	}

	[Server]
	public void GiveRandomEquipment()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveRandomEquipment()' called on client");
			return;
		}
		PickupIndex pickupIndex = Run.instance.availableEquipmentDropList[UnityEngine.Random.Range(0, Run.instance.availableEquipmentDropList.Count)];
		SetEquipmentIndex(PickupCatalog.GetPickupDef(pickupIndex)?.equipmentIndex ?? EquipmentIndex.None, isRemovingEquipment: true);
	}

	[Server]
	public void GiveRandomEquipment(Xoroshiro128Plus rng)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveRandomEquipment(Xoroshiro128Plus)' called on client");
			return;
		}
		PickupIndex pickupIndex = rng.NextElementUniform(Run.instance.availableEquipmentDropList);
		SetEquipmentIndex(PickupCatalog.GetPickupDef(pickupIndex)?.equipmentIndex ?? EquipmentIndex.None, isRemovingEquipment: true);
	}

	private void SetItemAcquiredServer(ItemIndex itemIndex, bool acquired)
	{
		ref bool reference = ref itemAcquisitionSet[(int)itemIndex];
		if (reference != acquired)
		{
			reference = acquired;
			if (acquired)
			{
				itemAcquisitionOrder.Add(itemIndex);
			}
			else
			{
				itemAcquisitionOrder.Remove(itemIndex);
			}
			SetDirtyBit(8u);
		}
	}

	public int CalculateEffectiveItemStacks(ItemIndex itemIndex)
	{
		return (int)Math.Clamp(0L + (long)permanentItemStacks.GetStackValue(itemIndex) + channeledItemStacks.GetStackValue(itemIndex) + tempItemsStorage.GetItemStacks(itemIndex), 0L, 2147483647L);
	}

	private void UpdateAllEffectiveItemStacks()
	{
		ItemIndex[] indicesToUpdateArray;
		FixedSizeArrayPool<ItemIndex>.DisposableRental disposableRental = ItemCatalog.PerItemBufferPool.RequestTemp<ItemIndex>(out indicesToUpdateArray);
		bool[] indicesToUpdateSet;
		int indicesToUpdateCount;
		try
		{
			FixedSizeArrayPool<bool>.DisposableRental disposableRental2 = ItemCatalog.PerItemBufferPool.RequestTemp<bool>(out indicesToUpdateSet);
			try
			{
				indicesToUpdateCount = 0;
				ReadOnlySpan<ItemIndex> nonZeroIndicesSpan = permanentItemStacks.GetNonZeroIndicesSpan();
				for (int i = 0; i < nonZeroIndicesSpan.Length; i++)
				{
					AddItem(nonZeroIndicesSpan[i]);
				}
				nonZeroIndicesSpan = channeledItemStacks.GetNonZeroIndicesSpan();
				for (int i = 0; i < nonZeroIndicesSpan.Length; i++)
				{
					AddItem(nonZeroIndicesSpan[i]);
				}
				nonZeroIndicesSpan = tempItemsStorage.GetNonZeroIndicesSpan();
				for (int i = 0; i < nonZeroIndicesSpan.Length; i++)
				{
					AddItem(nonZeroIndicesSpan[i]);
				}
				nonZeroIndicesSpan = effectiveItemStacks.GetNonZeroIndicesSpan();
				for (int i = 0; i < nonZeroIndicesSpan.Length; i++)
				{
					AddItem(nonZeroIndicesSpan[i]);
				}
				Span<ItemIndex> span = MemoryExtensions.AsSpan(indicesToUpdateArray, 0, indicesToUpdateCount);
				for (int i = 0; i < span.Length; i++)
				{
					ItemIndex itemIndex = span[i];
					UpdateEffectiveItemStacks(itemIndex);
				}
			}
			finally
			{
				disposableRental2.Dispose();
			}
		}
		finally
		{
			disposableRental.Dispose();
		}
		void AddItem(ItemIndex itemIndex2)
		{
			if (!indicesToUpdateSet[(int)itemIndex2])
			{
				indicesToUpdateSet[(int)itemIndex2] = true;
				indicesToUpdateArray[indicesToUpdateCount++] = itemIndex2;
			}
		}
	}

	private void UpdateEffectiveItemStacks(ItemIndex itemIndex)
	{
		bool flag = false;
		int num = 0;
		int stackValue = permanentItemStacks.GetStackValue(itemIndex);
		int stackValue2 = channeledItemStacks.GetStackValue(itemIndex);
		int itemStacks = tempItemsStorage.GetItemStacks(itemIndex);
		flag = flag || stackValue > 0;
		flag = flag || stackValue2 > 0;
		flag = flag || itemStacks > 0;
		num += stackValue;
		num += stackValue2;
		num += itemStacks;
		num = (int)Math.Clamp(num, 0L, 2147483647L);
		if (inventoryDisabled && ItemCatalog.GetItemDef(itemIndex).canRemove)
		{
			num = 0;
		}
		effectiveItemStacks.GetStackValue(itemIndex);
		effectiveItemStacks.SetStackValue(itemIndex, num);
		if (NetworkServer.active)
		{
			SetItemAcquiredServer(itemIndex, flag);
		}
	}

	[Server]
	private void ChangeItemStacksCount<TImpl>(TImpl impl, ItemIndex itemIndex, int countToAdd) where TImpl : struct, IChangeItemCountImpl
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ChangeItemStacksCount(TImpl,RoR2.ItemIndex,System.Int32)' called on client");
		}
		else
		{
			if (!ItemCatalog.IsIndexValid(in itemIndex))
			{
				return;
			}
			Run instance = Run.instance;
			if ((object)instance != null && instance.IsItemExpansionLocked(itemIndex))
			{
				return;
			}
			long num = impl.GetStackCount(itemIndex);
			countToAdd = (int)(Math.Clamp(num + countToAdd, impl.allowNegative ? int.MinValue : 0, 2147483647L) - num);
			if (countToAdd == 0)
			{
				return;
			}
			using (new InventoryChangeScope(this))
			{
				impl.Add(itemIndex, countToAdd);
				UpdateEffectiveItemStacks(itemIndex);
			}
			if (countToAdd > 0)
			{
				Inventory.onServerItemGiven?.Invoke(this, itemIndex, countToAdd);
				if (spawnedOverNetwork)
				{
					CallRpcItemAdded(itemIndex);
				}
			}
		}
	}

	[Obsolete("Use .GiveItemPermanent instead.", false)]
	[Server]
	public void GiveItem(ItemIndex itemIndex, int countToAdd = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItem(RoR2.ItemIndex,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(itemIndex, countToAdd);
		}
	}

	[Server]
	public void GiveItemPermanent(ItemIndex itemIndex, int countToAdd = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemPermanent(RoR2.ItemIndex,System.Int32)' called on client");
			return;
		}
		ThrowIfInvalid();
		ChangeItemStacksCount(new GiveItemPermanentImpl
		{
			inventory = this
		}, itemIndex, countToAdd);
	}

	[Server]
	public void GiveItemChanneled(ItemIndex itemIndex, int countToAdd = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemChanneled(RoR2.ItemIndex,System.Int32)' called on client");
			return;
		}
		ThrowIfInvalid();
		ChangeItemStacksCount(new GiveItemChanneledImpl
		{
			inventory = this
		}, itemIndex, countToAdd);
	}

	[Server]
	public void GiveItemTemp(ItemIndex itemIndex, float countToAdd = 1f)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemTemp(RoR2.ItemIndex,System.Single)' called on client");
			return;
		}
		ThrowIfInvalid();
		tempItemsStorage.GiveItemTemp(itemIndex, countToAdd);
	}

	[Obsolete("Use .GiveItemPermanent instead.", false)]
	[Server]
	public void GiveItem(ItemDef itemDef, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItem(RoR2.ItemDef,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(itemDef?.itemIndex ?? ItemIndex.None, count);
		}
	}

	[Server]
	public void GiveItemPermanent(ItemDef itemDef, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::GiveItemPermanent(RoR2.ItemDef,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(itemDef?.itemIndex ?? ItemIndex.None, count);
		}
	}

	[ClientRpc]
	private void RpcItemAdded(ItemIndex itemIndex)
	{
		this.onItemAddedClient?.Invoke(itemIndex);
	}

	[ClientRpc]
	private void RpcClientEquipmentChanged(EquipmentIndex newEquipIndex, uint slot)
	{
		this.onEquipmentChangedClient?.Invoke(newEquipIndex, slot);
	}

	[Server]
	public void RemoveEquipment(EquipmentIndex equipmentIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveEquipment(RoR2.EquipmentIndex)' called on client");
			return;
		}
		for (int i = 0; i < _equipmentStateSlots.Length; i++)
		{
			for (int j = 0; j < _equipmentStateSlots[i].Length; j++)
			{
				if (_equipmentStateSlots[i][j].equipmentIndex == equipmentIndex)
				{
					if (j < _equipmentStateSlots[i].Length - 1)
					{
						_equipmentStateSlots[i][j] = _equipmentStateSlots[i][_equipmentStateSlots[i].Length - 1];
					}
					SetEquipmentIndexForSlot(EquipmentIndex.None, (uint)i, (uint)j);
				}
			}
		}
	}

	public bool HasEquipment(EquipmentIndex equipmentIndex)
	{
		for (int i = 0; i < _equipmentStateSlots.Length; i++)
		{
			for (int j = 0; j < _equipmentStateSlots[i].Length; j++)
			{
				if (_equipmentStateSlots[i][j].equipmentIndex == equipmentIndex)
				{
					return true;
				}
			}
		}
		return false;
	}

	[Obsolete("Use .RemoveItemPermanent instead.", false)]
	[Server]
	public void RemoveItem(ItemIndex itemIndex, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItem(RoR2.ItemIndex,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(itemIndex, -count);
		}
	}

	[Server]
	public void RemoveItemPermanent(ItemIndex itemIndex, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItemPermanent(RoR2.ItemIndex,System.Int32)' called on client");
		}
		else
		{
			GiveItemPermanent(itemIndex, -count);
		}
	}

	[Server]
	public void RemoveItemTemp(ItemIndex itemIndex, float count = 1f)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItemTemp(RoR2.ItemIndex,System.Single)' called on client");
		}
		else
		{
			GiveItemTemp(itemIndex, 0f - count);
		}
	}

	[Server]
	public void RemoveItemChanneled(ItemIndex itemIndex, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItemChanneled(RoR2.ItemIndex,System.Int32)' called on client");
		}
		else
		{
			GiveItemChanneled(itemIndex, -count);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[Server]
	[Obsolete("Use .RemoveItemPermanent instead.", false)]
	public void RemoveItem(ItemDef itemDef, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItem(RoR2.ItemDef,System.Int32)' called on client");
		}
		else
		{
			RemoveItemPermanent(itemDef?.itemIndex ?? ItemIndex.None, count);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[Server]
	public void RemoveItemPermanent(ItemDef itemDef, int count = 1)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::RemoveItemPermanent(RoR2.ItemDef,System.Int32)' called on client");
		}
		else
		{
			RemoveItemPermanent(itemDef?.itemIndex ?? ItemIndex.None, count);
		}
	}

	[Obsolete("Use .ResetItemPermanent instead.", false)]
	[Server]
	public void ResetItem(ItemIndex itemIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ResetItem(RoR2.ItemIndex)' called on client");
		}
		else
		{
			ResetItemPermanent(itemIndex);
		}
	}

	[Server]
	public void ResetItemPermanent(ItemIndex itemIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ResetItemPermanent(RoR2.ItemIndex)' called on client");
			return;
		}
		int stackValue = permanentItemStacks.GetStackValue(itemIndex);
		RemoveItemPermanent(itemIndex, stackValue);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[Server]
	[Obsolete("Use .ResetItemPermanent instead.", false)]
	public void ResetItem(ItemDef itemDef)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ResetItem(RoR2.ItemDef)' called on client");
		}
		else
		{
			ResetItemPermanent(itemDef?.itemIndex ?? ItemIndex.None);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[Server]
	public void ResetItemPermanent(ItemDef itemDef)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ResetItemPermanent(RoR2.ItemDef)' called on client");
		}
		else
		{
			ResetItemPermanent(itemDef?.itemIndex ?? ItemIndex.None);
		}
	}

	[Server]
	public void ResetItemTemp(ItemIndex itemIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ResetItemTemp(RoR2.ItemIndex)' called on client");
		}
		else
		{
			tempItemsStorage.ResetItem(itemIndex);
		}
	}

	[Server]
	[Obsolete("Use CopyEquipmentFrom(Inventory other, bool includeChargeData) going forward.")]
	public void CopyEquipmentFrom(Inventory other)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CopyEquipmentFrom(RoR2.Inventory)' called on client");
		}
		else
		{
			CopyEquipmentFrom(other, includeChargeData: false);
		}
	}

	[Server]
	public void CopyEquipmentFrom(Inventory other, bool includeChargeData)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CopyEquipmentFrom(RoR2.Inventory,System.Boolean)' called on client");
			return;
		}
		for (int i = 0; i < other._equipmentStateSlots.Length; i++)
		{
			for (int j = 0; j < other._equipmentStateSlots[i].Length; j++)
			{
				if (includeChargeData)
				{
					SetEquipment(new EquipmentState(other._equipmentStateSlots[i][j].equipmentIndex, other._equipmentStateSlots[i][j].chargeFinishTime, other._equipmentStateSlots[i][j].charges), (uint)i, (uint)j);
				}
				else
				{
					SetEquipment(new EquipmentState(other._equipmentStateSlots[i][j].equipmentIndex, Run.FixedTimeStamp.negativeInfinity, 1), (uint)i, (uint)j);
				}
			}
		}
	}

	public void FixExtraEquipmentCounts()
	{
		_lastExtraEquipmentCount = GetItemCountEffective(DLC3Content.Items.ExtraEquipment.itemIndex);
		if (_equipmentStateSlots.Length != 0)
		{
			int i;
			for (i = _lastExtraEquipmentCount + 1 - _equipmentStateSlots[0].Length; i > 0; i--)
			{
				AddEquipmentSet();
			}
			for (; i < 0; i++)
			{
				RemoveEquipmentSet();
			}
		}
	}

	private static bool DefaultItemCopyFilter(ItemIndex itemIndex)
	{
		return !ItemCatalog.GetItemDef(itemIndex).ContainsTag(ItemTag.CannotCopy);
	}

	[Server]
	public void AddItemsFrom([NotNull] Inventory other)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddItemsFrom(RoR2.Inventory)' called on client");
		}
		else
		{
			AddItemsFrom(other, defaultItemCopyFilterDelegate);
		}
	}

	[Server]
	public void AddItemsFrom([NotNull] Inventory other, [NotNull] Func<ItemIndex, bool> filter)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddItemsFrom(RoR2.Inventory,System.Func`2<RoR2.ItemIndex,System.Boolean>)' called on client");
			return;
		}
		using (new InventoryChangeScope(this))
		{
			List<ItemIndex> result;
			CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result);
			try
			{
				other.permanentItemStacks.GetNonZeroIndices(result);
				foreach (ItemIndex item in result)
				{
					ChangeItemStacksCount(new GiveItemPermanentImpl
					{
						inventory = this
					}, item, filter(item) ? other.permanentItemStacks.GetStackValue(item) : 0);
				}
			}
			finally
			{
				disposableRental.Dispose();
			}
		}
	}

	[Server]
	public void AddItemsFrom([NotNull] int[] otherItemStacks, [NotNull] Func<ItemIndex, bool> filter)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddItemsFrom(System.Int32[],System.Func`2<RoR2.ItemIndex,System.Boolean>)' called on client");
			return;
		}
		using (new InventoryChangeScope(this))
		{
			ReadOnlySpan<int> readOnlySpan = MemoryExtensions.AsSpan(otherItemStacks);
			for (ItemIndex itemIndex = ItemIndex.Count; (int)itemIndex < readOnlySpan.Length; itemIndex++)
			{
				int num = otherItemStacks[(int)itemIndex];
				if (num > 0 && filter(itemIndex))
				{
					ChangeItemStacksCount(new GiveItemPermanentImpl
					{
						inventory = this
					}, itemIndex, num);
				}
			}
		}
	}

	[Server]
	private void AddItemAcquisitionOrderFrom([NotNull] List<ItemIndex> otherItemAcquisitionOrder)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::AddItemAcquisitionOrderFrom(System.Collections.Generic.List`1<RoR2.ItemIndex>)' called on client");
			return;
		}
		int[] array = ItemCatalog.RequestItemStackArray();
		for (int i = 0; i < itemAcquisitionOrder.Count; i++)
		{
			ItemIndex itemIndex = itemAcquisitionOrder[i];
			array[(int)itemIndex] = 1;
		}
		int j = 0;
		for (int count = otherItemAcquisitionOrder.Count; j < count; j++)
		{
			ItemIndex itemIndex2 = otherItemAcquisitionOrder[j];
			ref int reference = ref array[(int)itemIndex2];
			if (reference == 0)
			{
				reference = 1;
				itemAcquisitionOrder.Add(itemIndex2);
			}
		}
		ItemCatalog.ReturnItemStackArray(array);
	}

	[Server]
	public void CloneItemInventory([NotNull] Inventory other)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CloneItemInventory(RoR2.Inventory)' called on client");
			return;
		}
		using (new InventoryChangeScope(this))
		{
			CleanInventory();
			CopyItemsFrom(other);
		}
	}

	[Server]
	public void CopyItemsFrom([NotNull] Inventory other)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CopyItemsFrom(RoR2.Inventory)' called on client");
		}
		else
		{
			CopyItemsFrom(other, defaultItemCopyFilterDelegate);
		}
	}

	[Server]
	public void CopyItemsFrom([NotNull] Inventory other, [NotNull] Func<ItemIndex, bool> filter)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CopyItemsFrom(RoR2.Inventory,System.Func`2<RoR2.ItemIndex,System.Boolean>)' called on client");
			return;
		}
		CleanInventory();
		AddItemsFrom(other, filter);
	}

	[Server]
	public void CleanInventory()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::CleanInventory()' called on client");
			return;
		}
		using (new InventoryChangeScope(this))
		{
			List<ItemIndex> result;
			CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result);
			try
			{
				permanentItemStacks.GetNonZeroIndices(result);
				permanentItemStacks.Clear();
				foreach (ItemIndex item in result)
				{
					UpdateEffectiveItemStacks(item);
				}
			}
			finally
			{
				disposableRental.Dispose();
			}
		}
	}

	[Server]
	public void ShrineRestackInventory([NotNull] Xoroshiro128Plus rng)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::ShrineRestackInventory(Xoroshiro128Plus)' called on client");
			return;
		}
		List<ItemIndex> result;
		CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result);
		try
		{
			List<ItemIndex> result2;
			CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental2 = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result2);
			try
			{
				bool flag = false;
				foreach (ItemTierDef allItemTierDef in ItemTierCatalog.allItemTierDefs)
				{
					if (!allItemTierDef.canRestack)
					{
						continue;
					}
					int num = 0;
					float num2 = 0f;
					result.Clear();
					result2.Clear();
					effectiveItemStacks.GetNonZeroIndices(result2);
					foreach (ItemIndex item in result2)
					{
						effectiveItemStacks.GetStackValue(item);
						ItemDef itemDef = ItemCatalog.GetItemDef(item);
						if (allItemTierDef.tier == itemDef.tier && itemDef.DoesNotContainTag(ItemTag.ObjectiveRelated) && itemDef.DoesNotContainTag(ItemTag.PowerShape))
						{
							num += GetItemCountPermanent(item);
							num2 += (float)GetItemCountTemp(item);
							result.Add(item);
							ResetItemPermanent(item);
							ResetItemTemp(item);
						}
					}
					if (result.Count > 0)
					{
						ItemIndex itemIndex = rng.NextElementUniform(result);
						GiveItemPermanent(itemIndex, num);
						GiveItemTemp(itemIndex, num2);
						flag = true;
					}
				}
				if (flag)
				{
					SetDirtyBit(8u);
				}
			}
			finally
			{
				disposableRental2.Dispose();
			}
		}
		finally
		{
			disposableRental.Dispose();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[CompilableComment("With the introduction of item disabling and temp items, GetItemCount had to redirect to either the behavior of .GetItemCountEffective (for item disabling, temp items, etc.) or .GetItemCountPermanent (for things like consumable items or purchases). Redirection to .GetItemCountEffective was chosen as it's expected to affect far fewer mods than the alternative. GiveItem and RemoveItem have been redirected to the permanent versions, as it's the only option that makes sense.")]
	[Obsolete("Use .GetItemCountEffective or .GetItemCountPermanent instead.", false)]
	public int GetItemCount(ItemIndex itemIndex)
	{
		return GetItemCountEffective(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[Obsolete("Use .GetItemCountEffective or .GetItemCountPermanent instead.", false)]
	public int GetItemCount(ItemDef itemDef)
	{
		return GetItemCountEffective(itemDef);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountEffective(ItemIndex itemIndex)
	{
		return effectiveItemStacks.GetStackValue(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountEffective(ItemDef itemDef)
	{
		return GetItemCountEffective(itemDef?.itemIndex ?? ItemIndex.None);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountPermanent(ItemIndex itemIndex)
	{
		return permanentItemStacks.GetStackValue(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountPermanent(ItemDef itemDef)
	{
		return GetItemCountPermanent(itemDef?.itemIndex ?? ItemIndex.None);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountChanneled(ItemIndex itemIndex)
	{
		return channeledItemStacks.GetStackValue(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountChanneled(ItemDef itemDef)
	{
		return GetItemCountChanneled(itemDef?.itemIndex ?? ItemIndex.None);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountTemp(ItemIndex itemIndex)
	{
		return tempItemsStorage.GetItemStacks(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int GetItemCountTemp(ItemDef itemDef)
	{
		return GetItemCountTemp(itemDef?.itemIndex ?? ItemIndex.None);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetTempItemDecayValue(ItemIndex itemIndex)
	{
		return tempItemsStorage.GetItemDecayValue(itemIndex);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetTempItemRawValue(ItemIndex itemIndex)
	{
		return tempItemsStorage.GetItemRawValue(itemIndex);
	}

	public int GetTotalItemCount()
	{
		return effectiveItemStacks.GetTotalItemStacks();
	}

	public int GetTotalTempItemCount()
	{
		return tempItemsStorage.GetTotalItemStacks();
	}

	public bool HasAtLeastXTotalRemovablePermanentItemsOfTier(ItemTier itemTier, int x)
	{
		return permanentItemStacks.HasAtLeastXTotalRemovableItemsOfTier(itemTier, x);
	}

	[Obsolete("Use HasAtLeastXTotalRemovablePermanentItemsOfTier instead.", false)]
	public bool HasAtLeastXTotalItemsOfTier(ItemTier itemTier, int x)
	{
		return HasAtLeastXTotalRemovablePermanentItemsOfTier(itemTier, x);
	}

	public int GetTotalItemCountOfTier(ItemTier itemTier)
	{
		int num = 0;
		ItemIndex itemIndex = ItemIndex.Count;
		for (ItemIndex itemCount = (ItemIndex)ItemCatalog.itemCount; itemIndex < itemCount; itemIndex++)
		{
			if (ItemCatalog.GetItemDef(itemIndex).tier == itemTier)
			{
				num += GetItemCountEffective(itemIndex);
			}
		}
		return num;
	}

	public void WriteItemStacks(int[] output)
	{
		Array.Clear(output, 0, output.Length);
		effectiveItemStacks.AddTo(output);
	}

	public void WriteAllPermanentItemStacks(Span<int> dest)
	{
		dest.Clear();
		permanentItemStacks.AddTo(dest);
	}

	public void WriteAllTempItemDecayValues(Span<float> dest)
	{
		dest.Clear();
		tempItemsStorage.WriteAllTempItemDecayValues(dest);
	}

	public void WriteAllTempItemRawValues(Span<float> dest)
	{
		dest.Clear();
		tempItemsStorage.WriteAllTempItemRawValues(dest);
	}

	[Server]
	public void SetItemDecayDurationServer(float duration)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::SetItemDecayDurationServer(System.Single)' called on client");
		}
		else
		{
			tempItemsStorage.SetDecayDurationServer(duration);
		}
	}

	public override int GetNetworkChannel()
	{
		return QosChannelIndex.defaultReliable.intVal;
	}

	public override void OnDeserialize(NetworkReader reader, bool initialState)
	{
		byte num = reader.ReadByte();
		bool flag = (num & 1) != 0;
		bool flag2 = (num & 4) != 0;
		bool flag3 = (num & 8) != 0;
		bool flag4 = (num & 0x10) != 0;
		bool flag5 = (num & 0x20) != 0;
		bool flag6 = (num & 0x40) != 0;
		bool flag7 = (num & 0x80) != 0;
		if (flag)
		{
			permanentItemStacks.Deserialize(reader);
		}
		if (flag2)
		{
			infusionBonus = reader.ReadPackedUInt32();
		}
		if (flag3)
		{
			uint num2 = reader.ReadPackedUInt32();
			itemAcquisitionOrder.Clear();
			itemAcquisitionOrder.Capacity = (int)num2;
			for (uint num3 = 0u; num3 < num2; num3++)
			{
				ItemIndex item = (ItemIndex)reader.ReadPackedUInt32();
				itemAcquisitionOrder.Add(item);
			}
		}
		if (flag4)
		{
			equipmentDisabled = reader.ReadBoolean();
			uint num4 = reader.ReadByte();
			for (uint num5 = 0u; num5 < num4; num5++)
			{
				uint num6 = reader.ReadByte();
				for (uint num7 = 0u; num7 < num6; num7++)
				{
					SetEquipmentInternal(EquipmentState.Deserialize(reader), num5, num7);
				}
			}
			activeEquipmentSlot = reader.ReadByte();
			for (uint num8 = 0u; num8 < num4; num8++)
			{
				_activeEquipmentSet[num8] = reader.ReadByte();
			}
		}
		if (flag5)
		{
			tempItemsStorage.Deserialize(reader);
		}
		if (flag6)
		{
			channeledItemStacks.Deserialize(reader);
		}
		if (flag7)
		{
			inventoryDisabled = reader.ReadBoolean();
		}
		if (flag || flag4 || flag2 || flag5 || flag6 || flag7)
		{
			UpdateAllEffectiveItemStacks();
			HandleInventoryChanged();
		}
	}

	public override bool OnSerialize(NetworkWriter writer, bool initialState)
	{
		uint num = base.syncVarDirtyBits;
		if (initialState)
		{
			num = 253u;
		}
		for (int i = 0; i < _equipmentStateSlots.Length; i++)
		{
			for (int j = 0; j < _equipmentStateSlots[i].Length; j++)
			{
				if (_equipmentStateSlots[i][j].dirty)
				{
					num |= 0x10;
					break;
				}
			}
		}
		if (_activeEquipmentDirty)
		{
			_activeEquipmentDirty = false;
			num |= 0x10;
		}
		bool num2 = (num & 1) != 0;
		bool flag = (num & 4) != 0;
		bool flag2 = (num & 8) != 0;
		bool flag3 = (num & 0x10) != 0;
		bool flag4 = (num & 0x20) != 0;
		bool flag5 = (num & 0x40) != 0;
		bool flag6 = (num & 0x80) != 0;
		writer.Write((byte)num);
		if (num2)
		{
			permanentItemStacks.Serialize(writer);
		}
		if (flag)
		{
			writer.WritePackedUInt32(infusionBonus);
		}
		if (flag2)
		{
			int count = itemAcquisitionOrder.Count;
			writer.WritePackedUInt32((uint)count);
			for (int k = 0; k < count; k++)
			{
				writer.WritePackedUInt32((uint)itemAcquisitionOrder[k]);
			}
		}
		if (flag3)
		{
			writer.Write(equipmentDisabled);
			writer.Write((byte)_equipmentStateSlots.Length);
			for (int l = 0; l < _equipmentStateSlots.Length; l++)
			{
				writer.Write((byte)_equipmentStateSlots[l].Length);
				for (int m = 0; m < _equipmentStateSlots[l].Length; m++)
				{
					EquipmentState.Serialize(writer, _equipmentStateSlots[l][m]);
				}
			}
			writer.Write(activeEquipmentSlot);
			for (int n = 0; n < _equipmentStateSlots.Length; n++)
			{
				writer.Write(_activeEquipmentSet[n]);
			}
		}
		if (flag4)
		{
			tempItemsStorage.Serialize(writer);
		}
		if (flag5)
		{
			channeledItemStacks.Serialize(writer);
		}
		if (flag6)
		{
			writer.Write(inventoryDisabled);
		}
		if (!initialState)
		{
			for (int num3 = 0; num3 < _equipmentStateSlots.Length; num3++)
			{
				for (int num4 = 0; num4 < _equipmentStateSlots[num3].Length; num4++)
				{
					_equipmentStateSlots[num3][num4].dirty = false;
				}
			}
		}
		if (!initialState)
		{
			return num != 0;
		}
		return false;
	}

	[Server]
	public bool TryTransformRandomItem(TryTransformRandomItemArgs args, out TryTransformRandomItemsResult result)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Boolean RoR2.Inventory::TryTransformRandomItem(RoR2.Inventory/TryTransformRandomItemArgs,RoR2.Inventory/TryTransformRandomItemsResult&)' called on client");
			result = default(TryTransformRandomItemsResult);
			return false;
		}
		result = default(TryTransformRandomItemsResult);
		using (new InventoryChangeScope(this))
		{
			List<(ItemIndex, ItemStorageType)> result2;
			CollectionPool<(ItemIndex, ItemStorageType), List<(ItemIndex, ItemStorageType)>>.DisposableRental disposableRental = CollectionPool<(ItemIndex, ItemStorageType), List<(ItemIndex, ItemStorageType)>>.RentCollection(out result2);
			try
			{
				if (!args.forbidPermanent)
				{
					List<ItemIndex> result3;
					CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental2 = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result3);
					try
					{
						permanentItemStacks.GetNonZeroIndices(result3);
						foreach (ItemIndex item in result3)
						{
							result2.Add((item, ItemStorageType.Permanent));
						}
					}
					finally
					{
						disposableRental2.Dispose();
					}
				}
				if (!args.forbidTemporary)
				{
					List<ItemIndex> result4;
					CollectionPool<ItemIndex, List<ItemIndex>>.DisposableRental disposableRental3 = CollectionPool<ItemIndex, List<ItemIndex>>.RentCollection(out result4);
					try
					{
						tempItemsStorage.GetNonZeroIndices(result4);
						foreach (ItemIndex item2 in result4)
						{
							result2.Add((item2, ItemStorageType.Temporary));
						}
					}
					finally
					{
						disposableRental3.Dispose();
					}
				}
				Util.ShuffleList(result2, args.rng);
				foreach (var item3 in result2)
				{
					ItemIndex itemIndex = args.filter(new TryTransformRandomItemArgs.FilterArgs
					{
						itemIndex = item3.Item1,
						itemStorageType = item3.Item2
					});
					if (ItemCatalog.IsIndexValid(in itemIndex))
					{
						switch (item3.Item2)
						{
						case ItemStorageType.Permanent:
							GiveItemPermanent(item3.Item1, -1);
							GiveItemPermanent(itemIndex);
							break;
						case ItemStorageType.Temporary:
							GiveItemTemp(item3.Item1, -1f);
							GiveItemTemp(itemIndex);
							break;
						default:
							return false;
						}
						result = new TryTransformRandomItemsResult
						{
							originalItemIndex = item3.Item1,
							originalItemStorageType = item3.Item2,
							newItemIndex = itemIndex
						};
						return true;
					}
				}
			}
			finally
			{
				disposableRental.Dispose();
			}
		}
		return false;
	}

	[Server]
	public void EnforceItemStackLimit(ItemIndex itemIndex, int maxStacks)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.Inventory::EnforceItemStackLimit(RoR2.ItemIndex,System.Int32)' called on client");
			return;
		}
		int itemCountTemp = GetItemCountTemp(itemIndex);
		int itemCountPermanent = GetItemCountPermanent(itemIndex);
		int num = itemCountTemp + itemCountPermanent - maxStacks;
		if (num > 0)
		{
			int num2 = Math.Min(num, itemCountTemp);
			if (num2 > 0)
			{
				RemoveItemTemp(itemIndex, num2);
				num -= num2;
			}
			if (num > 0)
			{
				RemoveItemPermanent(itemIndex, num);
			}
		}
	}

	private void ThrowIfInvalid()
	{
		if (!this)
		{
			throw new MissingReferenceException("Inventory cannot be accessed or modified after destruction.");
		}
	}

	private void UNetVersion()
	{
	}

	protected static void InvokeCmdCmdSwitchToNextEquipmentInSet(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			Debug.LogError("Command CmdSwitchToNextEquipmentInSet called on client.");
		}
		else
		{
			((Inventory)obj).CmdSwitchToNextEquipmentInSet();
		}
	}

	protected static void InvokeCmdCmdSwitchToPreviousEquipmentInSet(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkServer.active)
		{
			Debug.LogError("Command CmdSwitchToPreviousEquipmentInSet called on client.");
		}
		else
		{
			((Inventory)obj).CmdSwitchToPreviousEquipmentInSet();
		}
	}

	public void CallCmdSwitchToNextEquipmentInSet()
	{
		if (!NetworkClient.active)
		{
			Debug.LogError("Command function CmdSwitchToNextEquipmentInSet called on server.");
			return;
		}
		if (base.isServer)
		{
			CmdSwitchToNextEquipmentInSet();
			return;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.Write((short)0);
		networkWriter.Write((short)5);
		networkWriter.WritePackedUInt32((uint)kCmdCmdSwitchToNextEquipmentInSet);
		networkWriter.Write(GetComponent<NetworkIdentity>().netId);
		SendCommandInternal(networkWriter, 0, "CmdSwitchToNextEquipmentInSet");
	}

	public void CallCmdSwitchToPreviousEquipmentInSet()
	{
		if (!NetworkClient.active)
		{
			Debug.LogError("Command function CmdSwitchToPreviousEquipmentInSet called on server.");
			return;
		}
		if (base.isServer)
		{
			CmdSwitchToPreviousEquipmentInSet();
			return;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.Write((short)0);
		networkWriter.Write((short)5);
		networkWriter.WritePackedUInt32((uint)kCmdCmdSwitchToPreviousEquipmentInSet);
		networkWriter.Write(GetComponent<NetworkIdentity>().netId);
		SendCommandInternal(networkWriter, 0, "CmdSwitchToPreviousEquipmentInSet");
	}

	protected static void InvokeRpcRpcItemAdded(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkClient.active)
		{
			Debug.LogError("RPC RpcItemAdded called on server.");
		}
		else
		{
			((Inventory)obj).RpcItemAdded((ItemIndex)reader.ReadInt32());
		}
	}

	protected static void InvokeRpcRpcClientEquipmentChanged(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkClient.active)
		{
			Debug.LogError("RPC RpcClientEquipmentChanged called on server.");
		}
		else
		{
			((Inventory)obj).RpcClientEquipmentChanged((EquipmentIndex)reader.ReadInt32(), reader.ReadPackedUInt32());
		}
	}

	public void CallRpcItemAdded(ItemIndex itemIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogError("RPC Function RpcItemAdded called on client.");
			return;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.Write((short)0);
		networkWriter.Write((short)2);
		networkWriter.WritePackedUInt32((uint)kRpcRpcItemAdded);
		networkWriter.Write(GetComponent<NetworkIdentity>().netId);
		networkWriter.Write((int)itemIndex);
		SendRPCInternal(networkWriter, 0, "RpcItemAdded");
	}

	public void CallRpcClientEquipmentChanged(EquipmentIndex newEquipIndex, uint slot)
	{
		if (!NetworkServer.active)
		{
			Debug.LogError("RPC Function RpcClientEquipmentChanged called on client.");
			return;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.Write((short)0);
		networkWriter.Write((short)2);
		networkWriter.WritePackedUInt32((uint)kRpcRpcClientEquipmentChanged);
		networkWriter.Write(GetComponent<NetworkIdentity>().netId);
		networkWriter.Write((int)newEquipIndex);
		networkWriter.WritePackedUInt32(slot);
		SendRPCInternal(networkWriter, 0, "RpcClientEquipmentChanged");
	}

	public override void PreStartClient()
	{
	}
}
