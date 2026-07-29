using UnityEngine;

public class Bullet : MonoBehaviour
{
	private Vector3 fwd;

	private void Start()
	{
		fwd = base.transform.InverseTransformDirection(Vector3.forward);
	}

	private void Update()
	{
		GetComponent<Rigidbody>().AddForce(fwd * 100f);
	}
}
