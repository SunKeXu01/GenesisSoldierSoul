using UnityEngine;

public class PZ : MonoBehaviour
{
	public GameObject option;

	public GameObject optione;

	private void OnTriggerEnter(Collider other)
	{
		option.SetActive(true);
		optione.SetActive(true);
	}
}
