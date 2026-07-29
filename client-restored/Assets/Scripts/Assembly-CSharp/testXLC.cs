using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class testXLC : MonoBehaviour
{
	public Text loadingText;

	public Image progressBar;

	public GameObject option;

	public GameObject optione;

	private int curProgressValue;

	private void Update()
	{
		Cursor.visible = false;
		Cursor.lockState = CursorLockMode.Locked;
		GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<Music1>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<StopGameXLC>().enabled = false;
		int num = 100;
		if (curProgressValue < num)
		{
			curProgressValue++;
		}
		loadingText.text = curProgressValue + "%";
		progressBar.fillAmount = (float)curProgressValue / 100f;
		if (curProgressValue == 100)
		{
			Thread.Sleep(250);
			if (option != null)
			{
				option.SetActive(false);
			}
			if (optione != null)
			{
				optione.SetActive(true);
			}
			GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Sound2>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Music1>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<StopGameXLC>().enabled = true;
		}
	}
}
