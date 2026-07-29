using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class vp_FXBullet : vp_Bullet
{
	public vp_ImpactEvent ImpactEvent;

	protected override void TrySpawnFX()
	{
		m_Transform.position = m_Hit.point;
		vp_SurfaceManager.SpawnEffect(m_Hit, ImpactEvent, m_Audio);
	}

	protected override void DoUFPSDamage()
	{
		vp_Bullet.m_TargetDHandler.Damage(new vp_DamageInfo(Damage, m_Source, vp_DamageInfo.DamageType.Bullet));
	}
}
