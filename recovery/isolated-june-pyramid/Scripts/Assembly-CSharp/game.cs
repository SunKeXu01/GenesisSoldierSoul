using UnityEngine;
using UnityEngine.UI;

public class game : MonoBehaviour
{
	public Image image_tab;

	public Image image_menu;

	public Button Button_Esc;

	public void open_tab()
	{
		image_tab.gameObject.SetActive(true);
	}

	public void close_tab()
	{
		image_tab.gameObject.SetActive(false);
	}

	public void open_menu()
	{
		image_menu.gameObject.SetActive(true);
	}

	public void close_menu()
	{
		image_menu.gameObject.SetActive(false);
	}

	private void Start()
	{
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Tab))
		{
			image_tab.gameObject.SetActive(true);
		}
		if (Input.GetKeyUp(KeyCode.Tab))
		{
			image_tab.gameObject.SetActive(false);
		}
		if (Input.GetKey(KeyCode.P))
		{
			image_menu.gameObject.SetActive(true);
		}
	}
}
