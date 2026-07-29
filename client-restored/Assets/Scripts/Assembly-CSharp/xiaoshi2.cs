using UnityEngine;

public class xiaoshi2 : MonoBehaviour
{
	private void OnCollisionEnter(Collision collision)
	{
		if (collision.gameObject.name == "pick")
		{
			GameObject.Find("First Person Player").GetComponent<xiao>().enabled = true;
		}
	}
}
