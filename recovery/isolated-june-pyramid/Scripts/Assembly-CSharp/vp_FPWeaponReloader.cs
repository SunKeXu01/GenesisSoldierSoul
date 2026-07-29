using UnityEngine;

[RequireComponent(typeof(vp_FPWeapon))]
public class vp_FPWeaponReloader : vp_WeaponReloader
{
	public AnimationClip AnimationReload;

	private vp_FPWeapon m_FPWeapon;

	private Animation m_WeaponAnimation;

	private vp_FPWeapon FPWeapon
	{
		get
		{
			if (m_FPWeapon == null)
			{
				m_FPWeapon = m_Weapon as vp_FPWeapon;
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

	protected override void OnStart_Reload()
	{
		base.OnStart_Reload();
		if (!(AnimationReload == null))
		{
			if (m_Player.Reload.AutoDuration == 0f)
			{
				m_Player.Reload.AutoDuration = AnimationReload.length;
			}
			WeaponAnimation.CrossFade(AnimationReload.name);
		}
	}
}
