using UnityEngine;

public class daojishi : MonoBehaviour
{
	private float lastTime;

	private float curTime;

	public GameObject option;

	public GameObject optione;

	public GameObject optionc;

	public GameObject optionce;

	public GameObject optiones;

	private void Start()
	{
		lastTime = Time.time;
		GameObject.Find("First Person Player/Main Camera").GetComponent<MouseLook>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<PlayMove>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<Music1>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<StopGame1>().enabled = false;
	}

	private void Update()
	{
		curTime = Time.time;
		if (curTime - lastTime >= 1f)
		{
			GameObject.Find("First Person Player/Main Camera").GetComponent<MouseLook>().enabled = true;
		}
		if (curTime - lastTime >= 2f)
		{
			optiones.SetActive(false);
			option.SetActive(true);
		}
		if (curTime - lastTime >= 3f)
		{
			option.SetActive(false);
			optione.SetActive(true);
		}
		if (curTime - lastTime >= 4f)
		{
			optione.SetActive(false);
			optionc.SetActive(true);
		}
		if (curTime - lastTime >= 5f)
		{
			optionc.SetActive(false);
			optionce.SetActive(true);
			optiones.SetActive(true);
			GameObject.Find("First Person Player").GetComponent<PlayMove>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Music1>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<daojishi>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<Esc>().enabled = false;
			GameObject.Find("First Person Player").GetComponent<StopGame1>().enabled = true;
		}
	}
}
