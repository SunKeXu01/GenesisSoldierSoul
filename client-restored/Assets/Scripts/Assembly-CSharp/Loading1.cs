using UnityEngine;

public class Loading1 : MonoBehaviour
{
	private bool transitioning;

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Return)
			|| Input.GetKeyDown(KeyCode.KeypadEnter)
			|| Input.GetKeyDown(KeyCode.Space))
		{
			GoToRoomList();
		}
		else if (Input.GetMouseButtonDown(0))
		{
			GoNextScene(gameObject);
		}
	}

	private void GoToRoomList()
	{
		if (transitioning)
		{
			return;
		}
		transitioning = true;
		Application.LoadLevel("Ziyou1");
	}

	public void GoNextScene(GameObject NScene)
	{
		if (transitioning)
		{
			return;
		}
		transitioning = true;
		Application.LoadLevel(1);
	}
}
