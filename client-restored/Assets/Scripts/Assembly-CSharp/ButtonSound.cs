using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonSound : MonoBehaviour, IPointerEnterHandler, IEventSystemHandler
{
	public AudioClip Sound;

	public Texture2D cursorTexture;

	private CursorMode cursorMode;

	private Vector2 hotSpot = Vector2.zero;

	public void OnPointerEnter(PointerEventData eventData)
	{
		Cursor.SetCursor(cursorTexture, hotSpot, cursorMode);
		AudioSource audioSource = GetComponent<AudioSource>();
		if (audioSource == null)
		{
			audioSource = base.gameObject.AddComponent<AudioSource>();
		}
		audioSource.playOnAwake = false;
		audioSource.clip = Sound;
		audioSource.Play();
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
	}
}
