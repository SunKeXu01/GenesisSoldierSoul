using UnityEngine;

public class Daoji : MonoBehaviour
{
	private void Start()
	{
		Object.Destroy(base.gameObject, 0.3f);
	}

	private void OnCollisionEnter(Collision collision)
	{
		Object.Destroy(base.gameObject);
	}
}
