using UnityEngine;

public class Test4 : MonoBehaviour
{
	public GameObject optionece;

	private float lastTime;

	private float curTime;

	private void Start()
	{
		lastTime = Time.time;
		GameObject.Find("First Person Player/Main Camera").GetComponent<MouseLook>().enabled = false;
		GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = false;
	}

	private void Update()
	{
		curTime = Time.time;
		if (curTime - lastTime >= 1f)
		{
			optionece.SetActive(true);
			GameObject.Find("First Person Player").GetComponent<PlayerMovement>().enabled = true;
			GameObject.Find("First Person Player/Main Camera").GetComponent<MouseLook>().enabled = true;
			GameObject.Find("First Person Player").GetComponent<Test4>().enabled = false;
		}
	}
}
