using UnityEngine;

public class AppearEnemy : MonoBehaviour
{
	public Transform prefab;

	public Transform role;

	private bool isAppear;

	private void Start()
	{
		for (int i = 0; i < 5; i++)
		{
			Object.Instantiate(prefab, new Vector3(base.transform.position.x + (float)i * 2f, base.transform.position.y, base.transform.position.z + (float)i * 2f), Quaternion.identity);
		}
	}

	private void Update()
	{
		if (Vector3.Distance(role.position, base.transform.position) < 20f && !isAppear)
		{
			isAppear = true;
			for (int i = 0; i < 5; i++)
			{
				Object.Instantiate(prefab, new Vector3(base.transform.position.x + (float)i * 2f, base.transform.position.y, base.transform.position.z + (float)i * 2f), Quaternion.identity);
			}
		}
	}
}
