// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupDropletController
using System;
using System.Runtime.InteropServices;
using RoR2;
using RoR2.Artifacts;
using Unity;
using UnityEngine;
using UnityEngine.Networking;

public class PickupDropletController : NetworkBehaviour
{
	[NonSerialized]
	[SyncVar]
	public UniquePickup pickupState = UniquePickup.none;

	private bool alive = true;

	private GenericPickupController.CreatePickupInfo createPickupInfo;

	private static GameObject commandCubePrefab;

	private static bool isCommandChest;

	private static GameObject pickupDropletPrefab;

	[Obsolete("Use pickupState instead.", false)]
	public PickupIndex pickupIndex
	{
		get
		{
			return pickupState.pickupIndex;
		}
		set
		{
			NetworkpickupState = new UniquePickup
			{
				pickupIndex = value
			};
		}
	}

	public UniquePickup NetworkpickupState
	{
		get
		{
			return pickupState;
		}
		[param: In]
		set
		{
			SetSyncVar(value, ref pickupState, 1u);
		}
	}

	[Obsolete]
	public static void CreatePickupDroplet(UniquePickup pickup, Vector3 position, Vector3 velocity)
	{
		CreatePickupDroplet(pickup, position, velocity, isDuplicated: false);
	}

	public static void CreatePickupDroplet(UniquePickup pickup, Vector3 position, Vector3 velocity, bool isDuplicated)
	{
		CreatePickupDroplet(new GenericPickupController.CreatePickupInfo
		{
			rotation = Quaternion.identity,
			pickup = pickup,
			position = position,
			duplicated = isDuplicated
		}, position, velocity);
	}

	public static void CreatePickupDroplet(UniquePickup pickup, Vector3 position, Vector3 velocity, bool isDuplicated, bool isRecycled)
	{
		CreatePickupDroplet(new GenericPickupController.CreatePickupInfo
		{
			rotation = Quaternion.identity,
			pickup = pickup,
			position = position,
			duplicated = isDuplicated,
			recycled = isRecycled
		}, position, velocity);
	}

	[Obsolete("Use the UniquePickup overload instead.", false)]
	public static void CreatePickupDroplet(PickupIndex pickupIndex, Vector3 position, Vector3 velocity)
	{
		CreatePickupDroplet(new UniquePickup
		{
			pickupIndex = pickupIndex
		}, position, velocity);
	}

	public static void CreatePickupDroplet(GenericPickupController.CreatePickupInfo pickupInfo, Vector3 position, Vector3 velocity)
	{
		if (CommandArtifactManager.IsCommandArtifactEnabled)
		{
			pickupInfo.artifactFlag |= GenericPickupController.PickupArtifactFlag.COMMAND;
		}
		GameObject obj = UnityEngine.Object.Instantiate(pickupDropletPrefab, position, Quaternion.identity);
		PickupDropletController component = obj.GetComponent<PickupDropletController>();
		if ((bool)component)
		{
			component.createPickupInfo = pickupInfo;
			component.NetworkpickupState = pickupInfo.pickup;
		}
		Rigidbody component2 = obj.GetComponent<Rigidbody>();
		component2.velocity = velocity;
		component2.AddTorque(UnityEngine.Random.Range(150f, 120f) * UnityEngine.Random.onUnitSphere);
		NetworkServer.Spawn(obj);
	}

	[InitDuringStartupPhase(GameInitPhase.PostProgressBar, 0)]
	private static void Init()
	{
		LegacyResourcesAPI.LoadAsyncCallback("Prefabs/NetworkedObjects/PickupDroplet", delegate(GameObject operationResult)
		{
			pickupDropletPrefab = operationResult;
		});
	}

	public static void IfCommandChestSpawned(bool value, PickupIndex pickupIndex, Vector3 position, Vector3 velocity)
	{
		isCommandChest = value;
		commandCubePrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/CommandCube");
		CreatePickupDroplet(pickupIndex, position, velocity);
	}

	public void OnCollisionEnter(Collision collision)
	{
		InitializePickup();
	}

	public void ForcePickupInitialization()
	{
		InitializePickup();
	}

	private void InitializePickup()
	{
		if (NetworkServer.active && alive)
		{
			alive = false;
			createPickupInfo.position = base.transform.position;
			CreatePickup();
			UnityEngine.Object.Destroy(base.gameObject);
		}
	}

	private void Start()
	{
		GameObject gameObject = PickupCatalog.GetPickupDef(pickupState.pickupIndex)?.dropletDisplayPrefab;
		if ((bool)gameObject)
		{
			UnityEngine.Object.Instantiate(gameObject, base.transform);
		}
	}

	private void CreatePickup()
	{
		if (createPickupInfo.artifactFlag.HasFlag(GenericPickupController.PickupArtifactFlag.COMMAND) && !createPickupInfo.artifactFlag.HasFlag(GenericPickupController.PickupArtifactFlag.DELUSION))
		{
			PickupDef pickupDef = PickupCatalog.GetPickupDef(createPickupInfo.pickup.pickupIndex);
			if (pickupDef != null && (pickupDef.itemIndex != ItemIndex.None || pickupDef.equipmentIndex != EquipmentIndex.None || pickupDef.itemTier != ItemTier.NoTier))
			{
				CreateCommandCube();
				return;
			}
		}
		GenericPickupController.CreatePickup(in createPickupInfo);
	}

	private void CreateCommandCube()
	{
		GameObject obj = UnityEngine.Object.Instantiate(CommandArtifactManager.commandCubePrefab, createPickupInfo.position, createPickupInfo.rotation);
		obj.GetComponent<PickupIndexNetworker>().NetworkpickupState = createPickupInfo.pickup;
		PickupPickerController component = obj.GetComponent<PickupPickerController>();
		component.SetOptionsFromPickupForCommandArtifact(createPickupInfo.pickup);
		component.chestGeneratedFrom = createPickupInfo.chest;
		NetworkServer.Spawn(obj);
	}

	private void UNetVersion()
	{
	}

	public override bool OnSerialize(NetworkWriter writer, bool forceAll)
	{
		if (forceAll)
		{
			GeneratedNetworkCode._WriteUniquePickup_None(writer, pickupState);
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
			GeneratedNetworkCode._WriteUniquePickup_None(writer, pickupState);
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
			pickupState = GeneratedNetworkCode._ReadUniquePickup_None(reader);
			return;
		}
		int num = (int)reader.ReadPackedUInt32();
		if ((num & 1) != 0)
		{
			pickupState = GeneratedNetworkCode._ReadUniquePickup_None(reader);
		}
	}

	public override void PreStartClient()
	{
	}
}
