using UnityEngine;

public class Hit : MonoBehaviour
{
	public GameObject BulletPre;

	public Transform BulletPoint;

	public float CD = 0.5f;

	private float timer;

	private void Start()
	{
	}

	private void Update()
	{
		timer += Time.deltaTime;
		if (Input.GetKeyDown(KeyCode.Mouse0))
		{
			Object.Instantiate(BulletPre, BulletPoint.position, BulletPoint.rotation);
		}
	}
}
