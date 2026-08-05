using UnityEngine;
using UnityEngine.UI;

public class Fangjian : MonoBehaviour
{
	public GameObject option;

	public void Click()
	{
		option.SetActive(false);
	}

	private void Start()
	{
		GameObject btnObj = GameObject.Find("Button18");
		if (btnObj == null)
		{
			// This recovered component is reused by scenes that do not expose
			// the optional legacy navigation button.
			return;
		}
		Button component = btnObj.GetComponent<Button>();
		if (component == null)
		{
			return;
		}
		component.onClick.AddListener(delegate
		{
			GoNextScene(btnObj);
		});
	}

	private void Update()
	{
	}

	public void GoNextScene(GameObject NScene)
	{
		Application.LoadLevel(14);
	}
}
