using UnityEngine;
using UnityEngine.UI;

public class testZhe : MonoBehaviour
{
	private float lastTime;

	private float curTime;

	public Image progressBar;

	public GameObject option;

	private int curProgressValue;

	private void Start()
	{
		lastTime = Time.time;
	}

	private void Update()
	{
		Cursor.visible = true;
		Cursor.lockState = CursorLockMode.None;
		int num = 100;
		if (curProgressValue < num)
		{
			curProgressValue++;
		}
		progressBar.fillAmount = (float)curProgressValue / 100f;
		curTime = Time.time;
		if (curTime - lastTime >= 3f)
		{
			option.SetActive(false);
		}
	}
}
