using UnityEngine;

public class BulletControl : MonoBehaviour
{
	private void Start()
	{
		GetComponent<Rigidbody>().velocity = base.transform.forward * 30f;
		Object.Destroy(base.gameObject, 3f);
	}

	private void OnCollisionEnter(Collision collision)
	{
		Object.Destroy(base.gameObject);
	}
}
