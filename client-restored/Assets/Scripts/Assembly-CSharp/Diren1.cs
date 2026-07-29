using UnityEngine;

public class Diren1 : MonoBehaviour
{
	public float HP = 100f;

	public GameObject option;

	public GameObject optione;

	public GameObject optiones;

	private void OnTriggerEnter(Collider other)
	{
		HP -= 100f;
		optiones.SetActive(true);
		if (HP <= 0f)
		{
			option.SetActive(true);
			optione.SetActive(true);
		}
	}
}
