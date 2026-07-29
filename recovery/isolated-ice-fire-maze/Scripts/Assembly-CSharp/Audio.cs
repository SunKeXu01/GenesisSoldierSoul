using UnityEngine;

public class Audio : MonoBehaviour
{
	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.W))
		{
			AudioClip clip = Resources.Load("Audios/Audio_move") as AudioClip;
			AudioSource audioSource = base.gameObject.GetComponent<AudioSource>();
			if (audioSource == null)
			{
				audioSource = base.gameObject.AddComponent<AudioSource>();
			}
			audioSource.clip = clip;
			audioSource.loop = true;
			audioSource.Play();
		}
		if (Input.GetKeyDown(KeyCode.Space))
		{
			AudioClip clip2 = Resources.Load("Audios/jump") as AudioClip;
			AudioSource audioSource2 = base.gameObject.GetComponent<AudioSource>();
			if (audioSource2 == null)
			{
				audioSource2 = base.gameObject.AddComponent<AudioSource>();
			}
			audioSource2.clip = clip2;
			audioSource2.loop = false;
			audioSource2.Play();
		}
		if (Input.GetMouseButton(0))
		{
			AudioClip clip3 = Resources.Load("Audios/emp") as AudioClip;
			AudioSource audioSource3 = base.gameObject.GetComponent<AudioSource>();
			if (audioSource3 == null)
			{
				audioSource3 = base.gameObject.AddComponent<AudioSource>();
			}
			audioSource3.clip = clip3;
			audioSource3.loop = false;
			audioSource3.Play();
		}
	}
}
