using UnityEngine;

public class Tab : MonoBehaviour
{
	public GameObject option;

	private void Update()
	{
		if (Input.GetKey(KeyCode.Tab))
		{
			option.SetActive(true);
		}
		if (Input.GetKeyUp(KeyCode.Tab))
		{
			option.SetActive(false);
		}
	}
}
