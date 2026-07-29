using UnityEngine;

public class JiXu3 : MonoBehaviour
{
	private bool isStop = true;

	public GameObject option;

	public GameObject optione;

	public void Click()
	{
		Time.timeScale = 1f;
		Cursor.visible = false;
		Cursor.lockState = CursorLockMode.Locked;
		isStop = true;
		optione.SetActive(true);
		option.SetActive(false);
		GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Music>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Tab>().enabled = true;
	}
}
