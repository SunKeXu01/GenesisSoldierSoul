using UnityEngine;

public class xiao : MonoBehaviour
{
	public GameObject option;

	public GameObject optione;

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.T))
		{
			option.SetActive(true);
			optione.SetActive(false);
		}
	}
}
