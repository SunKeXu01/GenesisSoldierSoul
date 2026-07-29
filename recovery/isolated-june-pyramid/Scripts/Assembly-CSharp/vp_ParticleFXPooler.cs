using UnityEngine;

public class vp_ParticleFXPooler : MonoBehaviour
{
	private ParticleSystem m_ShurikenParticleSystem;

	private bool m_IsShuriken;

	private void Awake()
	{
		m_ShurikenParticleSystem = GetComponent<ParticleSystem>();
		if (m_ShurikenParticleSystem != null)
		{
			m_IsShuriken = true;
		}
		if (!m_IsShuriken)
		{
			base.enabled = false;
			Object.Destroy(this);
		}
	}

	private void OnEnable()
	{
	}

	private void Update()
	{
		if (m_IsShuriken && !m_ShurikenParticleSystem.IsAlive())
		{
			vp_Utility.Destroy(base.gameObject);
		}
	}
}
