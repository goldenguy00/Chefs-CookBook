// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.GenericPickupController
using System;
using System.Runtime.InteropServices;
using RoR2;
using RoR2.Items;
using RoR2.Networking;
using RoR2.UI;
using Unity;
using UnityEngine;
using UnityEngine.Networking;

public sealed class GenericPickupController : NetworkBehaviour, IInteractable, IInspectable, IInspectInfoProvider, IDisplayNameProvider
{
	private class PickupMessage : MessageBase
	{
		public GameObject masterGameObject;

		public UniquePickup pickupState;

		public uint pickupQuantity;

		public void Reset()
		{
			masterGameObject = null;
			pickupState = UniquePickup.none;
			pickupQuantity = 0u;
		}

		public override void Serialize(NetworkWriter writer)
		{
			writer.Write(masterGameObject);
			GeneratedNetworkCode._WriteUniquePickup_None(writer, pickupState);
			writer.WritePackedUInt32(pickupQuantity);
		}

		public override void Deserialize(NetworkReader reader)
		{
			masterGameObject = reader.ReadGameObject();
			pickupState = GeneratedNetworkCode._ReadUniquePickup_None(reader);
			pickupQuantity = reader.ReadPackedUInt32();
		}
	}

	public enum PickupArtifactFlag
	{
		NONE,
		COMMAND,
		DELUSION
	}

	public struct CreatePickupInfo
	{
		public Vector3 position;

		public Quaternion rotation;

		private UniquePickup? _pickupState;

		public PickupPickerController.Option[] pickerOptions;

		public GameObject prefabOverride;

		public ChestBehavior chest;

		public PickupArtifactFlag artifactFlag;

		public ItemIndex delusionItemIndex;

		public ItemIndex falseChoice1;

		public ItemIndex falseChoice2;

		public bool duplicated;

		public bool recycled;

		[Obsolete("Use pickupState instead.", false)]
		public PickupIndex pickupIndex
		{
			get
			{
				return pickup.pickupIndex;
			}
			set
			{
				pickup = new UniquePickup
				{
					pickupIndex = value
				};
			}
		}

		public UniquePickup pickup
		{
			get
			{
				return _pickupState ?? UniquePickup.none;
			}
			set
			{
				_pickupState = value;
			}
		}
	}

	public PickupDisplay pickupDisplay;

	public ChestBehavior chestGeneratedFrom;

	[SyncVar(hook = "SyncPickupState")]
	private UniquePickup _pickupState = UniquePickup.none;

	[SyncVar(hook = "SyncRecycled")]
	public bool Recycled;

	[SyncVar(hook = "SyncDuplicated")]
	public bool Duplicated;

	public bool selfDestructIfPickupIndexIsNotIdeal;

	public SerializablePickupIndex idealPickupIndex;

	private static readonly PickupMessage pickupMessageInstance = new PickupMessage();

	public float waitDuration = 0.5f;

	private Run.FixedTimeStamp waitStartTime;

	private bool consumed;

	public const string pickupSoundString = "Play_UI_item_pickup";

	private static GameObject pickupPrefab;

	public UniquePickup pickup
	{
		get
		{
			return _pickupState;
		}
		set
		{
			Network_pickupState = value;
		}
	}

	[Obsolete("Use pickupState instead.", false)]
	public PickupIndex pickupIndex
	{
		get
		{
			return pickup.pickupIndex;
		}
		set
		{
			Network_pickupState = new UniquePickup
			{
				pickupIndex = value
			};
		}
	}

	public UniquePickup Network_pickupState
	{
		get
		{
			return _pickupState;
		}
		[param: In]
		set
		{
			if (NetworkServer.localClientActive && !base.syncVarHookGuard)
			{
				base.syncVarHookGuard = true;
				SyncPickupState(value);
				base.syncVarHookGuard = false;
			}
			SetSyncVar(value, ref _pickupState, 1u);
		}
	}

	public bool NetworkRecycled
	{
		get
		{
			return Recycled;
		}
		[param: In]
		set
		{
			if (NetworkServer.localClientActive && !base.syncVarHookGuard)
			{
				base.syncVarHookGuard = true;
				SyncRecycled(value);
				base.syncVarHookGuard = false;
			}
			SetSyncVar(value, ref Recycled, 2u);
		}
	}

	public bool NetworkDuplicated
	{
		get
		{
			return Duplicated;
		}
		[param: In]
		set
		{
			if (NetworkServer.localClientActive && !base.syncVarHookGuard)
			{
				base.syncVarHookGuard = true;
				SyncDuplicated(value);
				base.syncVarHookGuard = false;
			}
			SetSyncVar(value, ref Duplicated, 4u);
		}
	}

	private void SyncPickupState(UniquePickup newPickupState)
	{
		Network_pickupState = newPickupState;
		UpdatePickupDisplay();
	}

	private void SyncRecycled(bool isRecycled)
	{
		NetworkRecycled = isRecycled;
	}

	private void SyncDuplicated(bool isDuplicated)
	{
		NetworkDuplicated = isDuplicated;
	}

	[Server]
	public static void SendPickupMessage(CharacterMaster master, UniquePickup pickupState)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.GenericPickupController::SendPickupMessage(RoR2.CharacterMaster,RoR2.UniquePickup)' called on client");
			return;
		}
		uint pickupQuantity = 1u;
		if ((bool)master.inventory)
		{
			ItemIndex itemIndex = PickupCatalog.GetPickupDef(pickupState.pickupIndex)?.itemIndex ?? ItemIndex.None;
			if (itemIndex != ItemIndex.None)
			{
				pickupQuantity = (uint)master.inventory.CalculateEffectiveItemStacks(itemIndex);
			}
		}
		try
		{
			PickupMessage msg = new PickupMessage
			{
				masterGameObject = master.gameObject,
				pickupState = pickupState,
				pickupQuantity = pickupQuantity
			};
			NetworkServer.SendByChannelToAll(57, msg, QosChannelIndex.chat.intVal);
		}
		catch (Exception arg)
		{
			Debug.Log($"Failed to send pickupMessage for pickupIndex {pickupState.pickupIndex} - gameObject {master.gameObject.name}\r\n{arg}");
		}
	}

	[NetworkMessageHandler(msgType = 57, client = true)]
	private static void HandlePickupMessage(NetworkMessage netMsg)
	{
		PickupMessage pickupMessage = pickupMessageInstance;
		netMsg.ReadMessage(pickupMessage);
		GameObject masterGameObject = pickupMessage.masterGameObject;
		UniquePickup pickupState = pickupMessage.pickupState;
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupState.pickupIndex);
		uint pickupQuantity = pickupMessage.pickupQuantity;
		pickupMessage.Reset();
		if (!masterGameObject)
		{
			return;
		}
		CharacterMaster component = masterGameObject.GetComponent<CharacterMaster>();
		if (!component)
		{
			return;
		}
		PlayerCharacterMasterController component2 = component.GetComponent<PlayerCharacterMasterController>();
		if ((bool)component2)
		{
			NetworkUser networkUser = component2.networkUser;
			if ((bool)networkUser)
			{
				networkUser.localUser?.userProfile.DiscoverPickup(pickupState.pickupIndex);
			}
		}
		CharacterBody body = component.GetBody();
		_ = (bool)body;
		ItemDef itemDef = ItemCatalog.GetItemDef(pickupDef?.itemIndex ?? ItemIndex.None);
		if (itemDef != null && itemDef.hidden)
		{
			return;
		}
		if (pickupState.pickupIndex != PickupIndex.none)
		{
			ItemIndex transformedItemIndex = ContagiousItemManager.GetTransformedItemIndex(itemDef?.itemIndex ?? ItemIndex.None);
			if (itemDef == null || transformedItemIndex == ItemIndex.None || component.inventory.GetItemCountEffective(transformedItemIndex) <= 0)
			{
				CharacterMasterNotificationQueue.PushPickupNotification(component, pickupState.pickupIndex, pickupState.isTempItem, pickupState.upgradeValue);
			}
		}
		string text = pickupDef?.nameToken ?? PickupCatalog.invalidPickupToken;
		if (pickupState.upgradeValue > 0)
		{
			text = Util.GetNameFromUpgradeCount(text, pickupState.upgradeValue);
		}
		Chat.AddPickupMessage(body, text, pickupDef?.baseColor ?? Color.black, pickupQuantity, pickupState.isTempItem);
		if ((bool)body)
		{
			Util.PlaySound("Play_UI_item_pickup", body.gameObject);
		}
	}

	public void StartWaitTime()
	{
		waitStartTime = Run.FixedTimeStamp.now;
	}

	private void OnTriggerStay(Collider other)
	{
		if (!NetworkServer.active || !(waitStartTime.timeSince >= waitDuration) || consumed)
		{
			return;
		}
		CharacterBody component = other.GetComponent<CharacterBody>();
		if (!component)
		{
			return;
		}
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickup.pickupIndex);
		if (pickupDef == null)
		{
			return;
		}
		ItemIndex itemIndex = pickupDef.itemIndex;
		if (itemIndex != ItemIndex.None)
		{
			ItemTierDef itemTierDef = ItemTierCatalog.GetItemTierDef(ItemCatalog.GetItemDef(itemIndex).tier);
			if (((bool)itemTierDef && (itemTierDef.pickupRules == ItemTierDef.PickupRules.ConfirmAll || (itemTierDef.pickupRules == ItemTierDef.PickupRules.ConfirmFirst && (bool)component.inventory && component.inventory.GetItemCountEffective(itemIndex) <= 0))) || (itemIndex == DLC3Content.Items.Junk.itemIndex && component.bodyIndex != DLC3Content.BodyPrefabs.DrifterBody.bodyIndex))
			{
				return;
			}
		}
		EquipmentIndex equipmentIndex = pickupDef.equipmentIndex;
		if ((equipmentIndex == EquipmentIndex.None || (!EquipmentCatalog.GetEquipmentDef(equipmentIndex).isLunar && (!component.inventory || !component.inventory.EquipmentSetFull()))) && pickupDef.coinValue == 0 && BodyHasPickupPermission(component, pickup))
		{
			AttemptGrant(component);
		}
	}

	private static bool BodyHasPickupPermission(CharacterBody body, UniquePickup pickupState)
	{
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupState.pickupIndex);
		bool flag = pickupDef != null && pickupDef.equipmentIndex != EquipmentIndex.None && body.isRemoteOp;
		bool flag2 = false;
		if (body.isRemoteOp && pickupDef.itemIndex != ItemIndex.None)
		{
			ItemDef itemDef = ItemCatalog.GetItemDef(pickupDef.itemIndex);
			if (itemDef.ContainsTag(ItemTag.PowerShape) || itemDef.ContainsTag(ItemTag.ObjectiveRelated))
			{
				flag2 = true;
			}
		}
		if ((bool)(body.masterObject ? body.masterObject.GetComponent<PlayerCharacterMasterController>() : null) && (bool)body.inventory && !flag)
		{
			return !flag2;
		}
		return false;
	}

	public bool ShouldIgnoreSpherecastForInteractibility(Interactor activator)
	{
		return false;
	}

	public string GetContextString(Interactor activator)
	{
		return string.Format(Language.GetString(PickupCatalog.GetPickupDef(pickup.pickupIndex)?.interactContextToken ?? string.Empty), GetDisplayName());
	}

	private void UpdatePickupDisplay()
	{
		if (!pickupDisplay)
		{
			return;
		}
		pickupDisplay.SetPickup(pickup);
		if ((bool)pickupDisplay.modelRenderer)
		{
			Highlight component = GetComponent<Highlight>();
			if ((bool)component)
			{
				component.targetRenderer = pickupDisplay.modelRenderer;
				component.ResetHighlight();
			}
		}
	}

	[Server]
	private void AttemptGrant(CharacterBody body)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.GenericPickupController::AttemptGrant(RoR2.CharacterBody)' called on client");
			return;
		}
		TeamComponent component = body.GetComponent<TeamComponent>();
		if (!component || component.teamIndex != TeamIndex.Player)
		{
			return;
		}
		UniquePickup pickupState = pickup;
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupState.pickupIndex);
		if (!body.inventory || pickupDef == null)
		{
			return;
		}
		Vector3 position = (pickupDisplay ? pickupDisplay.transform.position : base.transform.position);
		Quaternion rotation = (pickupDisplay ? pickupDisplay.transform.rotation : Quaternion.identity);
		PickupDef.GrantContext context = new PickupDef.GrantContext
		{
			body = body,
			controller = this,
			position = position,
			rotation = rotation
		};
		pickupDef.attemptGrant?.Invoke(ref context);
		if ((bool)body)
		{
			CharacterBody.PickupClass pickupClass = CharacterBody.PickupClass.Item;
			if (pickupState.isTempItem)
			{
				pickupClass = CharacterBody.PickupClass.TempItem;
			}
			body.OnPickup(pickupClass);
		}
		consumed = context.shouldDestroy;
		if (context.shouldNotify)
		{
			SendPickupMessage(body.master, pickupState);
		}
		if ((bool)chestGeneratedFrom && DelusionChestController.isDelusionEnable)
		{
			chestGeneratedFrom.CallRpcSetDelusionPickupIndex(pickupState.pickupIndex);
		}
		if (context.shouldDestroy)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private void Start()
	{
		waitStartTime = Run.FixedTimeStamp.now;
		consumed = false;
		UpdatePickupDisplay();
	}

	private void OnEnable()
	{
		InstanceTracker.Add(this);
	}

	private void OnDisable()
	{
		InstanceTracker.Remove(this);
	}

	public Interactability GetInteractability(Interactor activator)
	{
		if (!base.enabled)
		{
			return Interactability.Disabled;
		}
		if (waitStartTime.timeSince < waitDuration || consumed)
		{
			return Interactability.Disabled;
		}
		CharacterBody component = activator.GetComponent<CharacterBody>();
		if ((bool)component)
		{
			if (PickupCatalog.GetPickupDef(pickup.pickupIndex)?.itemIndex == DLC3Content.Items.Junk.itemIndex)
			{
				if (component.bodyIndex != DLC3Content.BodyPrefabs.DrifterBody.bodyIndex)
				{
					return Interactability.Disabled;
				}
				return Interactability.Available;
			}
			if (!BodyHasPickupPermission(component, pickup))
			{
				return Interactability.Disabled;
			}
			return Interactability.Available;
		}
		return Interactability.Disabled;
	}

	public void OnInteractionBegin(Interactor activator)
	{
		AttemptGrant(activator.GetComponent<CharacterBody>());
	}

	public bool ShouldShowOnScanner()
	{
		return true;
	}

	public bool ShouldProximityHighlight()
	{
		return true;
	}

	public string GetDisplayName()
	{
		string text = Language.GetString(PickupCatalog.GetPickupDef(pickup.pickupIndex)?.nameToken ?? PickupCatalog.invalidPickupToken);
		if (pickup.upgradeValue != 0)
		{
			text = Util.GetNameFromUpgradeCount(text, pickup.upgradeValue);
		}
		return text;
	}

	public void SetPickupIndexFromString(string pickupString)
	{
		if (NetworkServer.active)
		{
			PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(pickupString);
			pickup = new UniquePickup
			{
				pickupIndex = pickupIndex
			};
		}
	}

	public void ForcePickupDisplayUpdate()
	{
		UpdatePickupDisplay();
	}

	[InitDuringStartupPhase(GameInitPhase.PostProgressBar, 0)]
	private static void Init()
	{
		LegacyResourcesAPI.LoadAsyncCallback("Prefabs/NetworkedObjects/GenericPickup", delegate(GameObject operationResult)
		{
			pickupPrefab = operationResult;
		});
	}

	public static GenericPickupController CreatePickup(in CreatePickupInfo createPickupInfo)
	{
		GameObject gameObject = UnityEngine.Object.Instantiate(createPickupInfo.prefabOverride ?? pickupPrefab, createPickupInfo.position, createPickupInfo.rotation);
		GenericPickupController component = gameObject.GetComponent<GenericPickupController>();
		if ((bool)component)
		{
			component.pickup = createPickupInfo.pickup;
			component.chestGeneratedFrom = createPickupInfo.chest;
		}
		else
		{
			PickupDisplay componentInChildren = gameObject.GetComponentInChildren<PickupDisplay>();
			if ((bool)componentInChildren)
			{
				GameObject modelObjectOverride = gameObject.GetComponentInChildren<MeshRenderer>().gameObject;
				componentInChildren.RebuildModel(modelObjectOverride);
			}
		}
		PickupIndexNetworker component2 = gameObject.GetComponent<PickupIndexNetworker>();
		if ((bool)component2)
		{
			component2.NetworkpickupState = createPickupInfo.pickup;
		}
		PickupPickerController component3 = gameObject.GetComponent<PickupPickerController>();
		if ((bool)component3 && createPickupInfo.pickerOptions != null)
		{
			component3.SetOptionsServer(createPickupInfo.pickerOptions);
		}
		if (createPickupInfo.duplicated)
		{
			component.NetworkDuplicated = createPickupInfo.duplicated;
		}
		if (createPickupInfo.recycled)
		{
			component.NetworkRecycled = createPickupInfo.recycled;
		}
		NetworkServer.Spawn(gameObject);
		return component;
	}

	[ContextMenu("Print Pickup Index")]
	private void PrintPickupIndex()
	{
		Debug.LogFormat("pickupIndex={0}", PickupCatalog.GetPickupDef(pickup.pickupIndex)?.internalName ?? "Invalid");
	}

	public IInspectInfoProvider GetInspectInfoProvider()
	{
		return this;
	}

	public bool CanBeInspected()
	{
		return pickup.pickupIndex.isValid;
	}

	public InspectInfo GetInfo()
	{
		InspectInfo inspectInfo = PickupCatalog.GetPickupDef(pickup.pickupIndex) ?? throw new InvalidOperationException("Attempted to get info for invalid pickup. Should be impossible. Investigate me.");
		inspectInfo.upgradeCount = pickup.upgradeValue;
		return inspectInfo;
	}

	private void UNetVersion()
	{
	}

	public override bool OnSerialize(NetworkWriter writer, bool forceAll)
	{
		if (forceAll)
		{
			GeneratedNetworkCode._WriteUniquePickup_None(writer, _pickupState);
			writer.Write(Recycled);
			writer.Write(Duplicated);
			return true;
		}
		bool flag = false;
		if ((base.syncVarDirtyBits & 1) != 0)
		{
			if (!flag)
			{
				writer.WritePackedUInt32(base.syncVarDirtyBits);
				flag = true;
			}
			GeneratedNetworkCode._WriteUniquePickup_None(writer, _pickupState);
		}
		if ((base.syncVarDirtyBits & 2) != 0)
		{
			if (!flag)
			{
				writer.WritePackedUInt32(base.syncVarDirtyBits);
				flag = true;
			}
			writer.Write(Recycled);
		}
		if ((base.syncVarDirtyBits & 4) != 0)
		{
			if (!flag)
			{
				writer.WritePackedUInt32(base.syncVarDirtyBits);
				flag = true;
			}
			writer.Write(Duplicated);
		}
		if (!flag)
		{
			writer.WritePackedUInt32(base.syncVarDirtyBits);
		}
		return flag;
	}

	public override void OnDeserialize(NetworkReader reader, bool initialState)
	{
		if (initialState)
		{
			_pickupState = GeneratedNetworkCode._ReadUniquePickup_None(reader);
			Recycled = reader.ReadBoolean();
			Duplicated = reader.ReadBoolean();
			return;
		}
		int num = (int)reader.ReadPackedUInt32();
		if ((num & 1) != 0)
		{
			SyncPickupState(GeneratedNetworkCode._ReadUniquePickup_None(reader));
		}
		if ((num & 2) != 0)
		{
			SyncRecycled(reader.ReadBoolean());
		}
		if ((num & 4) != 0)
		{
			SyncDuplicated(reader.ReadBoolean());
		}
	}

	public override void PreStartClient()
	{
	}
}
