using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class tofsjq : MonoBehaviour
{
	public Slider _progress;

	public Text loadingText;

	private void Awake()
	{
		_progress = GetComponent<Slider>();
	}

	private void Start()
	{
		StartCoroutine(LoadScene());
	}

	private IEnumerator LoadScene()
	{
		int disableProgress = 0;
		AsyncOperation op = SceneManager.LoadSceneAsync("Scenefsjq");
		op.allowSceneActivation = false;
		int toProgress;
		while (op.progress < 0.9f)
		{
			toProgress = (int)(op.progress * 100f);
			while (disableProgress < toProgress)
			{
				int num = disableProgress + 1;
				disableProgress = num;
				_progress.value = (float)disableProgress / 100f;
				yield return new WaitForEndOfFrame();
			}
		}
		toProgress = 100;
		while (disableProgress < toProgress)
		{
			int num = disableProgress + 1;
			disableProgress = num;
			_progress.value = (float)disableProgress / 100f;
			loadingText.text = (int)(_progress.value * 100f) + "%";
			yield return new WaitForEndOfFrame();
		}
		op.allowSceneActivation = true;
	}
}
