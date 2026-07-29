using UnityEngine;

public class JiXu : MonoBehaviour
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
		GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Sound1>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Music1>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Music>().enabled = true;
		GameObject.Find("First Person Player").GetComponent<Tab>().enabled = true;
	}
}
