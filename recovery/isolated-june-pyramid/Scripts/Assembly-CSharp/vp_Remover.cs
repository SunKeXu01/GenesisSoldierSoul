using UnityEngine;

public class vp_Remover : MonoBehaviour
{
	public float LifeTime = 10f;

	protected vp_Timer.Handle m_DestroyTimer = new vp_Timer.Handle();

	private void OnEnable()
	{
		vp_Timer.In(Mathf.Max(LifeTime, 0.1f), delegate
		{
			vp_Utility.Destroy(base.gameObject);
		}, m_DestroyTimer);
	}

	private void OnDisable()
	{
		m_DestroyTimer.Cancel();
	}
}
