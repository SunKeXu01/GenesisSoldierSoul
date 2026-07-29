using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class vp_SurfaceEffect : vp_Effect
{
	[Serializable]
	public struct DecalSection
	{
		public List<GameObject> m_Prefabs;

		[Range(0.1f, 2f)]
		public float MinScale;

		[Range(0.1f, 2f)]
		public float MaxScale;

		[Range(0f, 0.5f)]
		public float AllowEdgeOverlap;
	}

	public DecalSection Decal;

	public static Vector3 FootprintDirection = NO_DIRECTION;

	public static bool FootprintFlip = false;

	public static bool FootprintVerifyGroundContact = false;

	public static Vector3 NO_DIRECTION = new Vector3(-99999f, -99999f, -99999f);

	protected GameObject m_DecalToSpawn;

	protected GameObject m_LastSpawnedDecal;

	public override void Init()
	{
		base.Init();
		Decal.MinScale = 1f;
		Decal.MaxScale = 1f;
		Decal.AllowEdgeOverlap = 0.25f;
	}

	public virtual void SpawnWithDecal(RaycastHit hit, AudioSource audioSource = null)
	{
		base.Spawn(hit, audioSource);
		if (Decal.m_Prefabs == null || Decal.m_Prefabs.Count == 0)
		{
			return;
		}
		do
		{
			m_DecalToSpawn = Decal.m_Prefabs[UnityEngine.Random.Range(0, Decal.m_Prefabs.Count)];
			if (m_DecalToSpawn == null)
			{
				return;
			}
		}
		while (Decal.m_Prefabs.Count > 1 && m_DecalToSpawn == m_LastSpawnedDecal);
		if (FootprintDirection == NO_DIRECTION)
		{
			vp_DecalManager.Spawn(m_DecalToSpawn, hit, Decal.AllowEdgeOverlap, UnityEngine.Random.Range(Decal.MinScale, Decal.MaxScale));
		}
		else
		{
			vp_DecalManager.SpawnFootprint(m_DecalToSpawn, hit, FootprintDirection, FootprintFlip, FootprintVerifyGroundContact, UnityEngine.Random.Range(Decal.MinScale, Decal.MaxScale));
			FootprintDirection = NO_DIRECTION;
		}
		m_LastSpawnedDecal = m_DecalToSpawn;
	}
}
