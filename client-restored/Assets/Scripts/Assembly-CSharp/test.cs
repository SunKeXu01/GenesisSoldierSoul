using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class test : MonoBehaviour
{
	public Text loadingText;

	public Image progressBar;

	public GameObject option;

	public GameObject optiones;

	public GameObject optionec;

	private int curProgressValue = 32;

	private void Update()
	{
		Cursor.visible = false;
		Cursor.lockState = CursorLockMode.Locked;
		int num = 100;
		if (curProgressValue < num)
		{
			curProgressValue++;
		}
		loadingText.text = curProgressValue + "%";
		progressBar.fillAmount = (float)curProgressValue / 100f;
		if (curProgressValue == 54)
		{
			Thread.Sleep(250);
		}
		if (curProgressValue == 95)
		{
			Thread.Sleep(250);
		}
		if (curProgressValue == 100)
		{
			Thread.Sleep(250);
			if (option != null)
			{
				option.SetActive(false);
			}
			if (optiones != null)
			{
				optiones.SetActive(true);
			}
			if (optionec != null)
			{
				optionec.SetActive(true);
			}
		}
	}
}
