using UnityEngine;

public class bullet : MonoBehaviour
{
	private void OnCollisionEnter(Collision collisionInfo)
	{
		Object.Destroy(base.gameObject);
	}

	private void Start()
	{
		float t = 0.4f;
		Object.Destroy(base.gameObject, t);
	}

	private void Update()
	{
	}
}
