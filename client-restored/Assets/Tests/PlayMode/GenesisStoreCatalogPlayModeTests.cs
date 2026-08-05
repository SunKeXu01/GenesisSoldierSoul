using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class GenesisStoreCatalogPlayModeTests
{
    [UnityTest]
    public IEnumerator RecoveredWarehouseOpensReadOnlyResourceCatalog()
    {
        SceneManager.LoadScene("Zhu");
        yield return null;
        yield return null;
        yield return null;
        yield return null;

        var storeButton = GameObject.Find(
            "Canvas/RawImage 1/RawImage/Button 7");
        Assert.That(storeButton, Is.Not.Null);
        storeButton.GetComponent<Button>().onClick.Invoke();
        yield return null;

        var catalog = GameObject.Find(
            "Canvas/RawImage 1/RawImage/GenesisRecoveredStoreCatalog");
        Assert.That(catalog, Is.Not.Null);
        var cards = catalog.transform.Cast<Transform>()
            .Count(item => item.name.StartsWith("Catalog_"));
        Assert.That(cards, Is.EqualTo(8));
        var buttons = catalog.GetComponentsInChildren<Button>(true);
        Assert.That(buttons.Length, Is.EqualTo(1));
        Assert.That(buttons[0].name, Is.EqualTo("CloseRecoveredCatalog"));
        Assert.That(catalog.transform.Find("EvidenceBoundary"), Is.Not.Null);

        var cleanup = SceneManager.CreateScene("StoreCatalogCleanup");
        SceneManager.SetActiveScene(cleanup);
        yield return SceneManager.UnloadSceneAsync("Zhu");
    }
}
