using UnityEngine;

public class Tank : MonoBehaviour
{
	public GameObject goBullet;

	private GameObject bullet;

	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetButtonDown("Fire1"))
		{
			bullet = Object.Instantiate(goBullet);
			bullet.transform.parent = base.transform;
		}
	}
}
