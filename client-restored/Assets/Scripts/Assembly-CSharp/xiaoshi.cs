using UnityEngine;

public class xiaoshi : MonoBehaviour
{
	private float lastTime;

	private float curTime;

	private void Start()
	{
		lastTime = Time.time;
	}

	private void Update()
	{
		curTime = Time.time;
		if (curTime - lastTime >= 150f)
		{
			GameObject.Find("First Person Player").GetComponent<xiao>().enabled = true;
		}
	}
}
