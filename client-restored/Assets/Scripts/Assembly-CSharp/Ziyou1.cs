using UnityEngine;
using UnityEngine.UI;

public class Ziyou1 : MonoBehaviour
{
	public GameObject option;

	public void Click()
	{
		option.SetActive(false);
	}

	private void Start()
	{
		GameObject btnObj = GameObject.Find("Button");
		if (btnObj == null)
		{
			Debug.LogWarning("Recovered button target was not found in scene: " + gameObject.scene.name);
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
		Application.LoadLevel(12);
	}
}
