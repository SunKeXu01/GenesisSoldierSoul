using UnityEngine;

public class Ziyou1 : MonoBehaviour
{
	public GameObject option;

	public void Click()
	{
		option.SetActive(false);
	}

	private void Update()
	{
	}

	public void GoNextScene(GameObject NScene)
	{
		Application.LoadLevel(12);
	}
}
