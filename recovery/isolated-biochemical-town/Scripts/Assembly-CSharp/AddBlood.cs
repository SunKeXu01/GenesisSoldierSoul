using UnityEngine;

public class AddBlood : MonoBehaviour
{
	public Transform role;

	private void Start()
	{
	}

	private void Update()
	{
		if (Vector3.Distance(role.position, base.transform.position) < 2f)
		{
			role.SendMessage("addBlood");
			Object.Destroy(base.gameObject);
		}
	}
}
