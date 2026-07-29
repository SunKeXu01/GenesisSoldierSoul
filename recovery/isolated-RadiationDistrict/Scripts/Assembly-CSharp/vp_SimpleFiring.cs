using UnityEngine;

public class vp_SimpleFiring : MonoBehaviour
{
	protected vp_PlayerEventHandler m_Player;

	protected virtual void Awake()
	{
		m_Player = (vp_PlayerEventHandler)base.transform.root.GetComponentInChildren(typeof(vp_PlayerEventHandler));
	}

	protected virtual void OnEnable()
	{
		if (m_Player != null)
		{
			m_Player.Register(this);
		}
	}

	protected virtual void OnDisable()
	{
		if (m_Player != null)
		{
			m_Player.Unregister(this);
		}
	}

	protected virtual void Update()
	{
		if (m_Player.Attack.Active)
		{
			m_Player.Fire.Try();
		}
	}
}
