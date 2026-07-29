using UnityEngine;

public class ShootAudio : MonoBehaviour
{
	private void Start()
	{
	}

	private void Update()
	{
	}

	public void shootAudio()
	{
		GetComponent<AudioSource>().Play();
	}
}
