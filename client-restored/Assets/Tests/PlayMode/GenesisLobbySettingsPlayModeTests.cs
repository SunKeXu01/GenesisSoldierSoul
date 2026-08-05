using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class GenesisLobbySettingsPlayModeTests
{
    [UnityTest]
    public IEnumerator RecoveredSettingsEntryCreatesFunctionalControls()
    {
        SceneManager.LoadScene("Zhu");
        yield return null;
        yield return null;
        yield return null;
        yield return null;

        var buttonObject = GameObject.Find(
            "Canvas/RawImage 1/RawImage/Button 11");
        Assert.That(buttonObject, Is.Not.Null);
        buttonObject.GetComponent<Button>().onClick.Invoke();
        yield return null;
        yield return null;

        var recoveredPanel = GameObject.Find(
            "Canvas/RawImage 1/RawImage/RawImage 2");
        Assert.That(recoveredPanel, Is.Not.Null);
        Assert.That(recoveredPanel.activeInHierarchy, Is.True);
        var functional = recoveredPanel.transform.Find(
            "GenesisFunctionalSettings");
        Assert.That(functional, Is.Not.Null);
        Assert.That(
            functional.GetComponentsInChildren<Button>(true).Length,
            Is.EqualTo(12));
        Assert.That(
            functional.GetComponentsInChildren<Text>(true).Length,
            Is.GreaterThanOrEqualTo(19));

        var cleanup = SceneManager.CreateScene("LobbySettingsCleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("Zhu");
    }
}
