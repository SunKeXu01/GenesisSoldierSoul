using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class DHM : MonoBehaviour
{
	public InputField AccountInput;

	public Text Mistake;

	public Text Register;

	public GameObject option;

	public void OnButton()
	{
		string text = AccountInput.text;
		if (text == "GD20191124")
		{
			Register.gameObject.SetActive(true);
			StartCoroutine(Disappear());
			option.SetActive(true);
		}
		else
		{
			Mistake.gameObject.SetActive(true);
			StartCoroutine(Disappear());
			option.SetActive(false);
		}
	}

	private IEnumerator Disappear()
	{
		yield return new WaitForSeconds(2f);
		Mistake.gameObject.SetActive(false);
		Register.gameObject.SetActive(false);
	}
}
