using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class vp_SurfaceType : ScriptableObject
{
	[Serializable]
	public struct ImpactFXInfo
	{
		public vp_ImpactEvent ImpactEvent;

		public vp_SurfaceEffect SurfaceEffect;

		public ImpactFXInfo(bool init)
		{
			ImpactEvent = null;
			SurfaceEffect = null;
		}
	}

	[SerializeField]
	public List<ImpactFXInfo> ImpactFX = new List<ImpactFXInfo>();

	[SerializeField]
	public bool AllowFootprints;

	[NonSerialized]
	protected bool m_CanHaveFootprints;

	[NonSerialized]
	protected bool m_CachedCanHaveFootprints;

	public void Init()
	{
		ImpactFX.Add(new ImpactFXInfo(true));
	}

	public bool CanHaveFootprints()
	{
		if (!m_CachedCanHaveFootprints)
		{
			if (!AllowFootprints)
			{
				m_CanHaveFootprints = false;
			}
			else
			{
				foreach (ImpactFXInfo item in ImpactFX)
				{
					if (!(item.SurfaceEffect == null) && item.SurfaceEffect.Decal.m_Prefabs.Count > 0)
					{
						m_CanHaveFootprints = true;
						break;
					}
				}
			}
			m_CachedCanHaveFootprints = true;
		}
		return m_CanHaveFootprints;
	}
}
