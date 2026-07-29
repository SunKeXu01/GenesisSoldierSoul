using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class vp_RigidbodyImpulse : MonoBehaviour
{
	public Vector3 RigidbodyForce = new Vector3(0f, 5f, 0f);

	public bool LocalForce;

	public float RigidbodySpin = 0.2f;

	protected Rigidbody m_Rigidbody;

	protected Rigidbody Rigidbody
	{
		get
		{
			if (m_Rigidbody == null)
			{
				m_Rigidbody = GetComponent<Rigidbody>();
			}
			return m_Rigidbody;
		}
	}

	protected virtual void OnEnable()
	{
		if (Rigidbody == null)
		{
			return;
		}
		if (RigidbodyForce != Vector3.zero)
		{
			if (!LocalForce)
			{
				m_Rigidbody.AddForce(RigidbodyForce, ForceMode.Impulse);
			}
			else
			{
				m_Rigidbody.AddForce(base.transform.root.TransformDirection(RigidbodyForce), ForceMode.Impulse);
			}
		}
		if (RigidbodySpin != 0f)
		{
			m_Rigidbody.AddRelativeTorque(Random.rotation.eulerAngles * ((Random.value < 0.5f) ? RigidbodySpin : (0f - RigidbodySpin)));
		}
	}
}
