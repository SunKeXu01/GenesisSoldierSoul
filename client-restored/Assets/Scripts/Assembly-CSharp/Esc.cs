using UnityEngine;

public class Esc : MonoBehaviour
{
	private bool isStop = true;

	public GameObject option;

	public GameObject optione;

	public GameObject optionec;

	private void Update()
	{
		if (isStop)
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				optionec.SetActive(false);
				Time.timeScale = 0f;
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
				isStop = false;
				option.SetActive(true);
				optione.SetActive(false);
				GameObject.Find("First Person Player").GetComponent<PlayMove>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Music1>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Music>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Tab>().enabled = false;
			}
		}
		else if (Input.GetKeyDown(KeyCode.Escape))
		{
			Time.timeScale = 0f;
			Cursor.visible = true;
			Cursor.lockState = CursorLockMode.None;
			isStop = false;
			option.SetActive(true);
			optione.SetActive(false);
			GameObject.Find("First Person Player").GetComponent<PlayMove>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<Music1>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<Music>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<Tab>().enabled = false;
		}
	}
}
