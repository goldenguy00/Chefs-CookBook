// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.PickupDisplay
using System;
using System.Collections.Generic;
using RoR2;
using RoR2.ContentManagement;
using UnityEngine;

public class PickupDisplay : MonoBehaviour
{
	[Tooltip("The vertical motion of the display model.")]
	public Wave verticalWave;

	public bool dontInstantiatePickupModel;

	[Tooltip("The speed in degrees/second at which the display model rotates about the y axis.")]
	public float spinSpeed = 75f;

	public GameObject tier1ParticleEffect;

	public GameObject tier2ParticleEffect;

	public GameObject tier3ParticleEffect;

	public GameObject equipmentParticleEffect;

	public GameObject lunarParticleEffect;

	public GameObject bossParticleEffect;

	public GameObject voidParticleEffect;

	public GameObject foodParticleEffect;

	[Tooltip("The particle system to tint.")]
	public ParticleSystem[] coloredParticleSystems;

	[Space(10f)]
	public GameObject temporaryItemIndicator;

	private UniquePickup pickupState = UniquePickup.none;

	private bool hidden;

	private MaterialPropertyBlock propertyStorage;

	public Highlight highlight;

	private bool renderersDisabled;

	private static readonly Vector3 idealModelBox = Vector3.one;

	private static readonly float idealVolume = idealModelBox.x * idealModelBox.y * idealModelBox.z;

	private GameObject modelObject;

	private GameObject modelPrefab;

	private float modelScale;

	private float localTime;

	private bool shouldUpdate = true;

	public Renderer modelRenderer { get; private set; }

	public List<Renderer> modelRenderers { get; private set; }

	private Vector3 localModelPivotPosition => Vector3.up * verticalWave.Evaluate(localTime);

	[Obsolete("Use the PickupState overload of SetPickup instead.", false)]
	public void SetPickupIndex(PickupIndex newPickupIndex, bool newHidden = false)
	{
		UniquePickup other = new UniquePickup
		{
			pickupIndex = newPickupIndex
		};
		if (!pickupState.Equals(other) || hidden != newHidden)
		{
			pickupState = other;
			hidden = newHidden;
			RebuildModel();
		}
	}

	public void SetPickup(in UniquePickup newPickupState, bool newHidden = false)
	{
		if (!pickupState.Equals(newPickupState) || hidden != newHidden)
		{
			pickupState = newPickupState;
			hidden = newHidden;
			RebuildModel();
		}
	}

	public PickupIndex GetPickupIndex()
	{
		return pickupState.pickupIndex;
	}

	private void DestroyModel()
	{
		if ((bool)modelObject)
		{
			UnityEngine.Object.Destroy(modelObject);
			modelObject = null;
			modelRenderer = null;
		}
	}

	public void RebuildModel(GameObject modelObjectOverride = null)
	{
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupState.pickupIndex);
		if (modelObjectOverride != null)
		{
			modelObject = modelObjectOverride;
		}
		else
		{
			AssetOrDirectReference<GameObject> assetOrDirectReference = new AssetOrDirectReference<GameObject>
			{
				loadOnAssigned = true
			};
			if (pickupDef != null)
			{
				assetOrDirectReference.address = pickupDef.displayPrefabReference;
				assetOrDirectReference.directRef = (hidden ? PickupCatalog.GetHiddenPickupDisplayPrefab() : pickupDef.displayPrefab);
			}
			GameObject gameObject = assetOrDirectReference.WaitForCompletion();
			if (modelPrefab != gameObject)
			{
				DestroyModel();
				modelPrefab = gameObject;
				modelScale = base.transform.lossyScale.x;
				if (!dontInstantiatePickupModel && modelPrefab != null)
				{
					modelObject = UnityEngine.Object.Instantiate(modelPrefab);
					modelRenderer = modelObject.GetComponentInChildren<Renderer>();
					if ((bool)modelRenderer)
					{
						modelObject.transform.rotation = Quaternion.identity;
						Vector3 size = modelRenderer.bounds.size;
						float num = size.x * size.y * size.z;
						if (num <= float.Epsilon)
						{
							Debug.LogError("PickupDisplay bounds are zero! This is not allowed!");
							num = 1f;
						}
						modelScale *= Mathf.Pow(idealVolume, 1f / 3f) / Mathf.Pow(num, 1f / 3f);
						if ((bool)highlight)
						{
							highlight.targetRenderer = modelRenderer;
							highlight.isOn = true;
							highlight.pickupState = pickupState;
							highlight.ResetHighlight();
						}
					}
					modelObject.transform.parent = base.transform;
					modelObject.transform.localPosition = localModelPivotPosition;
					modelObject.transform.localRotation = Quaternion.identity;
					modelObject.transform.localScale = new Vector3(modelScale, modelScale, modelScale);
				}
			}
		}
		if ((bool)tier1ParticleEffect)
		{
			tier1ParticleEffect.SetActive(value: false);
		}
		if ((bool)tier2ParticleEffect)
		{
			tier2ParticleEffect.SetActive(value: false);
		}
		if ((bool)tier3ParticleEffect)
		{
			tier3ParticleEffect.SetActive(value: false);
		}
		if ((bool)equipmentParticleEffect)
		{
			equipmentParticleEffect.SetActive(value: false);
		}
		if ((bool)lunarParticleEffect)
		{
			lunarParticleEffect.SetActive(value: false);
		}
		if ((bool)voidParticleEffect)
		{
			voidParticleEffect.SetActive(value: false);
		}
		if ((bool)foodParticleEffect)
		{
			foodParticleEffect.SetActive(value: false);
		}
		ItemIndex itemIndex = pickupDef?.itemIndex ?? ItemIndex.None;
		EquipmentIndex equipmentIndex = pickupDef?.equipmentIndex ?? EquipmentIndex.None;
		if (itemIndex != ItemIndex.None)
		{
			switch (ItemCatalog.GetItemDef(itemIndex).tier)
			{
			case ItemTier.Tier1:
				if ((bool)tier1ParticleEffect)
				{
					tier1ParticleEffect.SetActive(value: true);
				}
				break;
			case ItemTier.Tier2:
				if ((bool)tier2ParticleEffect)
				{
					tier2ParticleEffect.SetActive(value: true);
				}
				break;
			case ItemTier.Tier3:
				if ((bool)tier3ParticleEffect)
				{
					tier3ParticleEffect.SetActive(value: true);
				}
				break;
			case ItemTier.VoidTier1:
			case ItemTier.VoidTier2:
			case ItemTier.VoidTier3:
			case ItemTier.VoidBoss:
				if ((bool)voidParticleEffect)
				{
					voidParticleEffect.SetActive(value: true);
				}
				break;
			case ItemTier.FoodTier:
				if ((bool)foodParticleEffect)
				{
					foodParticleEffect.SetActive(value: true);
				}
				break;
			}
		}
		else if (equipmentIndex != EquipmentIndex.None && (bool)equipmentParticleEffect)
		{
			equipmentParticleEffect.SetActive(value: true);
		}
		if ((bool)bossParticleEffect)
		{
			bossParticleEffect.SetActive(pickupDef?.isBoss ?? false);
		}
		if ((bool)lunarParticleEffect)
		{
			lunarParticleEffect.SetActive(pickupDef?.isLunar ?? false);
		}
		if ((bool)highlight)
		{
			highlight.isOn = true;
			highlight.pickupState = pickupState;
		}
		ParticleSystem[] array = coloredParticleSystems;
		foreach (ParticleSystem obj in array)
		{
			obj.gameObject.SetActive(modelPrefab != null);
			ParticleSystem.MainModule main = obj.main;
			main.startColor = pickupDef?.baseColor ?? PickupCatalog.invalidPickupColor;
		}
		if (pickupState.isTempItem && (bool)temporaryItemIndicator)
		{
			if ((bool)modelObject)
			{
				temporaryItemIndicator.transform.SetParent(modelObject.transform);
				OverridePlacementPosition component = modelObject.GetComponent<OverridePlacementPosition>();
				if (component != null)
				{
					temporaryItemIndicator.transform.localPosition = component.targetPosition.localPosition;
				}
			}
			temporaryItemIndicator.SetActive(value: true);
		}
		if (modelRenderers != null)
		{
			modelRenderers.Clear();
		}
		modelRenderers = new List<Renderer>();
		if (modelObject != null)
		{
			Renderer[] componentsInChildren = modelObject.GetComponentsInChildren<Renderer>();
			foreach (Renderer item in componentsInChildren)
			{
				modelRenderers.Add(item);
			}
		}
		int upgradeValue = pickupState.upgradeValue;
		DroneIndex droneIndex = pickupDef?.droneIndex ?? DroneIndex.None;
		if (upgradeValue > 0 && droneIndex != DroneIndex.None)
		{
			CharacterModel.DroneIndexEmissionColorPair droneUpgradePair = CharacterModel.GetDroneUpgradePair(upgradeValue - 1);
			for (int j = 0; j < modelRenderers.Count; j++)
			{
				Renderer renderer = modelRenderers[j];
				if ((bool)renderer)
				{
					renderer.GetPropertyBlock(propertyStorage);
					propertyStorage.SetFloat(CommonShaderProperties._EliteIndex, droneUpgradePair.rampIndex + 1);
					if (droneUpgradePair.rampIndex != -1)
					{
						propertyStorage.SetColor(CommonShaderProperties._EmColor, droneUpgradePair.emissionColor);
					}
					renderer.SetPropertyBlock(propertyStorage);
				}
			}
			base.transform.localScale = Vector3.one * DroneUpgradeUtils.GetScaleFromUpgradeCount(upgradeValue);
		}
		if (renderersDisabled)
		{
			SetRenderersEnabled(shouldEnable: false);
		}
	}

	public void SetRenderersEnabled(bool shouldEnable)
	{
		renderersDisabled = !shouldEnable;
		if ((bool)highlight)
		{
			highlight.enabled = shouldEnable;
		}
		if (modelRenderers == null)
		{
			return;
		}
		foreach (Renderer modelRenderer in modelRenderers)
		{
			if ((bool)modelRenderer)
			{
				modelRenderer.forceRenderingOff = renderersDisabled;
			}
		}
	}

	[Obsolete("Use SetRenderersEnabled instead.")]
	public void ToggleRenderersVisibility()
	{
		if ((bool)highlight)
		{
			highlight.enabled = !highlight.enabled;
		}
		foreach (Renderer modelRenderer in modelRenderers)
		{
			modelRenderer.forceRenderingOff = !modelRenderer.forceRenderingOff;
		}
	}

	private void Awake()
	{
		propertyStorage = new MaterialPropertyBlock();
	}

	private void Start()
	{
		localTime = 0f;
	}

	private void OnBecameVisible()
	{
		shouldUpdate = true;
	}

	private void OnBecameInvisible()
	{
		shouldUpdate = false;
	}

	private void Update()
	{
		if (shouldUpdate)
		{
			localTime += Time.deltaTime;
			if ((bool)modelObject)
			{
				Transform obj = modelObject.transform;
				Vector3 localEulerAngles = obj.localEulerAngles;
				localEulerAngles.y = spinSpeed * localTime;
				obj.localEulerAngles = localEulerAngles;
				obj.localPosition = localModelPivotPosition;
			}
		}
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = Color.yellow;
		Matrix4x4 matrix = Gizmos.matrix;
		Gizmos.matrix = Matrix4x4.TRS(base.transform.position, base.transform.rotation, base.transform.lossyScale);
		Gizmos.DrawWireCube(Vector3.zero, idealModelBox);
		Gizmos.matrix = matrix;
	}
}
