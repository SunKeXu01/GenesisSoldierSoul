using UnityEngine;
using UnityEngine.UI;

public class CXG1 : MonoBehaviour
{
	private bool isStop = true;

	private void Start()
	{
		GameObject btnObj = GameObject.Find("Button.");
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
		Time.timeScale = 1f;
		isStop = true;
	}

	public void GoNextScene(GameObject NScene)
	{
		Application.LoadLevel(2);
	}
}
