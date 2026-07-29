using UnityEngine;

public class Shoot : MonoBehaviour
{
	private int speed = 20;

	public Rigidbody Bullet;

	public Rigidbody fpoint;

	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetMouseButtonDown(0))
		{
			Object.Instantiate(Bullet, fpoint.position, fpoint.rotation).velocity = base.transform.TransformDirection(Vector3.forward * speed);
		}
	}
}
