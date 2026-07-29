using UnityEngine;

[RequireComponent(typeof(vp_FPWeapon))]
public class vp_FPWeaponShooter : vp_WeaponShooter
{
	public float MotionPositionReset = 0.5f;

	public float MotionRotationReset = 0.5f;

	public float MotionPositionPause = 1f;

	public float MotionRotationPause = 1f;

	public float MotionRotationRecoilCameraFactor;

	public float MotionPositionRecoilCameraFactor;

	public AnimationClip AnimationFire;

	public AnimationClip AnimationOutOfAmmo;

	protected vp_FPWeapon m_FPWeapon;

	private Animation m_WeaponAnimation;

	protected vp_FPCamera m_FPCamera;

	protected vp_FPPlayerEventHandler Player
	{
		get
		{
			if (m_Player == null && base.EventHandler != null)
			{
				m_Player = (vp_FPPlayerEventHandler)base.EventHandler;
			}
			return (vp_FPPlayerEventHandler)m_Player;
		}
	}

	public vp_FPWeapon FPWeapon
	{
		get
		{
			if (m_FPWeapon == null)
			{
				m_FPWeapon = base.transform.GetComponent<vp_FPWeapon>();
			}
			return m_FPWeapon;
		}
	}

	public Animation WeaponAnimation
	{
		get
		{
			if (m_WeaponAnimation == null)
			{
				if (FPWeapon == null)
				{
					return null;
				}
				if (FPWeapon.WeaponModel == null)
				{
					return null;
				}
				m_WeaponAnimation = FPWeapon.WeaponModel.GetComponent<Animation>();
			}
			return m_WeaponAnimation;
		}
	}

	public vp_FPCamera FPCamera
	{
		get
		{
			if (m_FPCamera == null)
			{
				m_FPCamera = base.transform.root.GetComponentInChildren<vp_FPCamera>();
			}
			return m_FPCamera;
		}
	}

	protected override void Awake()
	{
		base.Awake();
		if (m_ProjectileSpawnPoint == null)
		{
			m_ProjectileSpawnPoint = FPCamera.gameObject;
		}
		m_ProjectileDefaultSpawnpoint = m_ProjectileSpawnPoint;
		m_NextAllowedFireTime = Time.time;
		ProjectileSpawnDelay = Mathf.Min(ProjectileSpawnDelay, ProjectileFiringRate - 0.1f);
	}

	protected override void OnEnable()
	{
		RefreshFirePoint();
		base.OnEnable();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
	}

	protected override void Start()
	{
		base.Start();
		if (ProjectileFiringRate == 0f && AnimationFire != null)
		{
			ProjectileFiringRate = AnimationFire.length;
		}
		if (ProjectileFiringRate == 0f && AnimationFire != null)
		{
			ProjectileFiringRate = AnimationFire.length;
		}
	}

	protected override void Fire()
	{
		m_LastFireTime = Time.time;
		if (AnimationFire != null)
		{
			if (WeaponAnimation[AnimationFire.name] == null)
			{
				Debug.LogError(string.Concat("Error (", this, ") No animation named '", AnimationFire.name, "' is listed in this prefab. Make sure the prefab has an 'Animation' component which references all the clips you wish to play on the weapon."));
			}
			else
			{
				WeaponAnimation[AnimationFire.name].time = 0f;
				WeaponAnimation.Sample();
				WeaponAnimation.Play(AnimationFire.name);
			}
		}
		if (MotionRecoilDelay == 0f)
		{
			ApplyRecoil();
		}
		else
		{
			vp_Timer.In(MotionRecoilDelay, ApplyRecoil);
		}
		base.Fire();
		if (AnimationOutOfAmmo != null && m_Player.CurrentWeaponAmmoCount.Get() == 0)
		{
			if (WeaponAnimation[AnimationOutOfAmmo.name] == null)
			{
				Debug.LogError(string.Concat("Error (", this, ") No animation named '", AnimationOutOfAmmo.name, "' is listed in this prefab. Make sure the prefab has an 'Animation' component which references all the clips you wish to play on the weapon."));
			}
			else
			{
				WeaponAnimation[AnimationOutOfAmmo.name].time = 0f;
				WeaponAnimation.Sample();
				WeaponAnimation.Play(AnimationOutOfAmmo.name);
			}
		}
	}

	protected override void ApplyRecoil()
	{
		FPWeapon.ResetSprings(MotionPositionReset, MotionRotationReset, MotionPositionPause, MotionRotationPause);
		if (MotionRotationRecoil.z == 0f)
		{
			FPWeapon.AddForce2(MotionPositionRecoil, MotionRotationRecoil);
			if (MotionPositionRecoilCameraFactor != 0f)
			{
				FPCamera.AddForce2(MotionPositionRecoil * MotionPositionRecoilCameraFactor);
			}
			return;
		}
		FPWeapon.AddForce2(MotionPositionRecoil, Vector3.Scale(MotionRotationRecoil, Vector3.one + Vector3.back) + ((Random.value < 0.5f) ? Vector3.forward : Vector3.back) * Random.Range(MotionRotationRecoil.z * MotionRotationRecoilDeadZone, MotionRotationRecoil.z));
		if (MotionPositionRecoilCameraFactor != 0f)
		{
			FPCamera.AddForce2(MotionPositionRecoil * MotionPositionRecoilCameraFactor);
		}
		if (MotionRotationRecoilCameraFactor != 0f)
		{
			FPCamera.AddRollForce(Random.Range(MotionRotationRecoil.z * MotionRotationRecoilDeadZone, MotionRotationRecoil.z) * MotionRotationRecoilCameraFactor * ((Random.value < 0.5f) ? 1f : (-1f)));
		}
	}

	protected virtual void OnMessage_CameraToggle3rdPerson()
	{
		RefreshFirePoint();
	}

	private void RefreshFirePoint()
	{
		if (Player.IsFirstPerson == null)
		{
			return;
		}
		if (Player.IsFirstPerson.Get())
		{
			m_ProjectileSpawnPoint = FPCamera.gameObject;
			if (base.MuzzleFlash != null)
			{
				base.MuzzleFlash.layer = 31;
			}
			m_MuzzleFlashSpawnPoint = null;
			m_ShellEjectSpawnPoint = null;
			Refresh();
		}
		else
		{
			m_ProjectileSpawnPoint = m_ProjectileDefaultSpawnpoint;
			if (base.MuzzleFlash != null)
			{
				base.MuzzleFlash.layer = 0;
			}
			m_MuzzleFlashSpawnPoint = null;
			m_ShellEjectSpawnPoint = null;
			Refresh();
		}
		if (Player.CurrentWeaponName.Get() != base.name)
		{
			m_ProjectileSpawnPoint = FPCamera.gameObject;
		}
	}
}
