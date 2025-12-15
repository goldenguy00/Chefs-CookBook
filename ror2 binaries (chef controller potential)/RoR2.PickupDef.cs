// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupDef
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class PickupDef
{
	public struct GrantContext
	{
		public CharacterBody body;

		public GenericPickupController controller;

		public bool shouldDestroy;

		public bool shouldNotify;

		public Vector3 position;

		public Quaternion rotation;

		public PickupIndex grantedItem;

		public readonly UniquePickup pickup => controller.pickup;

		public readonly PickupIndex pickupIndex => pickup.pickupIndex;
	}

	public delegate void AttemptGrantDelegate(ref GrantContext context);

	public string internalName;

	public GameObject displayPrefab;

	public AssetReferenceT<GameObject> displayPrefabReference;

	public GameObject dropletDisplayPrefab;

	public string nameToken = "???";

	public Color baseColor;

	public Color darkColor;

	public ItemIndex itemIndex = ItemIndex.None;

	public EquipmentIndex equipmentIndex = EquipmentIndex.None;

	public ArtifactIndex artifactIndex = ArtifactIndex.None;

	public MiscPickupIndex miscPickupIndex = MiscPickupIndex.None;

	public DroneIndex droneIndex = DroneIndex.None;

	public ItemTier itemTier = ItemTier.NoTier;

	public uint coinValue;

	public UnlockableDef unlockableDef;

	public string interactContextToken;

	public bool isLunar;

	public bool isBoss;

	public Texture iconTexture;

	public Sprite iconSprite;

	public AttemptGrantDelegate attemptGrant;

	public PickupIndex pickupIndex { get; set; } = PickupIndex.none;
}
