using UnityEngine;

public class StopGameXLC : MonoBehaviour
{
	private bool isStop = true;

	public GameObject option;

	private void Update()
	{
		if (isStop)
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				Time.timeScale = 0f;
				Cursor.visible = true;
				Cursor.lockState = CursorLockMode.None;
				isStop = false;
				option.SetActive(true);
				GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = false;
				GameObject.Find("First Person Player").GetComponent<Music1>().enabled = false;
			}
		}
		else if (Input.GetKeyDown(KeyCode.Escape))
		{
			Time.timeScale = 1f;
			Cursor.visible = false;
			Cursor.lockState = CursorLockMode.Locked;
			isStop = true;
			option.SetActive(false);
			GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Music1>().enabled = true;
		}
	}
}
