using System;
using System.Collections.Generic;
using System.Reflection;
using GenesisSoldierSoul.Multiplayer;
using UnityEditor;
using UnityEngine;

public static class GenesisLobbyRadarAudit
{
    private static readonly string[] ExpectedMaps =
    {
        "pyramid",
        "newconstructionsite",
        "biochemicaltown",
        "classicconstructionsite",
        "steelfactory",
        "icefiremaze",
        "radiationdistrict",
    };

    private static readonly string[] ExpectedScenes =
    {
        "Pyramid",
        "NewConstructionSite",
        "BiochemicalTown",
        "ClassicConstructionSite",
        "SteelFactory",
        "IceFireMaze",
        "RadiationDistrict",
    };

    [MenuItem("Genesis/Audit Lobby Map Selection And Radar")]
    public static void ValidateLobbyAndRadar()
    {
        var lobbyType = typeof(GenesisLobbySession).Assembly.GetType(
            "GenesisSoldierSoul.Multiplayer.GenesisLobbyRooms", true);
        var mapField = lobbyType.GetField(
            "MapIds", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var normalizeMethod = lobbyType.GetMethod(
            "NormalizeMap", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var sceneMethod = lobbyType.GetMethod(
            "SceneName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (mapField == null || normalizeMethod == null || sceneMethod == null)
            throw new InvalidOperationException("Lobby map API is incomplete.");

        var actualMaps = mapField.GetValue(null) as string[];
        if (actualMaps == null || actualMaps.Length != ExpectedMaps.Length)
            throw new InvalidOperationException("Lobby must expose exactly seven maps.");
        var unique = new HashSet<string>(actualMaps, StringComparer.Ordinal);
        if (unique.Count != ExpectedMaps.Length)
            throw new InvalidOperationException("Lobby map identifiers contain duplicates.");

        for (var index = 0; index < ExpectedMaps.Length; index += 1)
        {
            if (actualMaps[index] != ExpectedMaps[index])
                throw new InvalidOperationException(
                    "Unexpected map order at " + index + ": " + actualMaps[index]);
            var scene = sceneMethod.Invoke(null, new object[] { actualMaps[index] }) as string;
            if (scene != ExpectedScenes[index])
                throw new InvalidOperationException(
                    actualMaps[index] + " resolves to " + scene);
        }

        var fallback = normalizeMethod.Invoke(null, new object[] { "unknown" }) as string;
        var normalized = normalizeMethod.Invoke(
            null, new object[] { "  STEELFACTORY " }) as string;
        if (fallback != "pyramid" || normalized != "steelfactory")
            throw new InvalidOperationException("Lobby map normalization is unsafe.");

        AssertVector(
            GenesisMatchController.CalculateRadarOffset(
                Vector3.zero, new Vector3(45f, 0f, 0f), 45f, 94f),
            new Vector2(94f, 0f), "east contact");
        AssertVector(
            GenesisMatchController.CalculateRadarOffset(
                Vector3.zero, new Vector3(0f, 0f, 45f), 45f, 94f),
            new Vector2(0f, 94f), "north contact");
        AssertVector(
            GenesisMatchController.CalculateRadarOffset(
                Vector3.zero, new Vector3(90f, 0f, 0f), 45f, 94f),
            new Vector2(94f, 0f), "clamped contact");

        ValidateHudLayouts();
        ValidateWarehousePreviews();

        Debug.Log(
            "[GenesisLobbyRadarAudit] PASS maps=7 sceneMappings=7 "
            + "radarRange=45 radarRadius=94 mode=free-for-all-enemies "
            + "hudViewports=7 warehousePreviews=3");
    }

    private static void AssertVector(Vector2 actual, Vector2 expected, string label)
    {
        if ((actual - expected).sqrMagnitude > 0.0001f)
            throw new InvalidOperationException(
                label + " expected " + expected + " but got " + actual);
    }

    private static void ValidateHudLayouts()
    {
        var resolutions = new[]
        {
            new Vector2(800f, 600f),
            new Vector2(1024f, 768f),
            new Vector2(1280f, 720f),
            new Vector2(1920f, 1080f),
            new Vector2(2560f, 1080f),
            new Vector2(3440f, 1440f),
            new Vector2(3840f, 2160f),
        };
        var panels = new[]
        {
            new HudPanel("health", Vector2.zero, new Vector2(20f, 18f), new Vector2(250f, 76f)),
            new HudPanel("ammo", new Vector2(1f, 0f), new Vector2(-20f, 18f), new Vector2(290f, 76f)),
            new HudPanel("score", new Vector2(0.5f, 1f), new Vector2(0f, -69f), new Vector2(250f, 30f)),
            new HudPanel("radar", new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(238f, 238f)),
            new HudPanel("radar-caption", new Vector2(0f, 1f), new Vector2(16f, -274f), new Vector2(300f, 30f)),
            new HudPanel("scoreboard", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 430f)),
            new HudPanel("kill-feed", Vector2.one, new Vector2(-20f, -78f), new Vector2(360f, 88f)),
            new HudPanel("weapon-bar", new Vector2(1f, 0f), new Vector2(-20f, 102f), new Vector2(290f, 34f)),
        };

        foreach (var resolution in resolutions)
        {
            var viewport = GenesisMatchController.CalculateHudReferenceViewport(resolution);
            foreach (var panel in panels)
            {
                var lower = Vector2.Scale(viewport, panel.anchor)
                    + panel.position - Vector2.Scale(panel.size, panel.anchor);
                var upper = lower + panel.size;
                if (lower.x < -0.01f || lower.y < -0.01f
                    || upper.x > viewport.x + 0.01f
                    || upper.y > viewport.y + 0.01f)
                {
                    throw new InvalidOperationException(
                        panel.name + " leaves viewport " + resolution
                        + " (reference " + viewport + ")");
                }
            }
        }
    }

    private static void ValidateWarehousePreviews()
    {
        var weapons = new[]
        {
            GenesisWeaponLoadout.M4A1,
            GenesisWeaponLoadout.M16,
            GenesisWeaponLoadout.Shotgun,
        };
        foreach (var weapon in weapons)
        {
            var resourcePath = GenesisWeaponLoadout.PreviewResourcePath(weapon);
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                throw new InvalidOperationException(
                    "Warehouse preview prefab is missing: " + resourcePath);
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException(
                    "Warehouse preview has no renderer: " + resourcePath);
            foreach (var renderer in renderers)
            {
                if (renderer.sharedMaterials == null
                    || Array.Exists(renderer.sharedMaterials, material => material == null))
                {
                    throw new InvalidOperationException(
                        "Warehouse preview has an empty material slot: " + resourcePath);
                }
            }
        }
    }

    private struct HudPanel
    {
        public readonly string name;
        public readonly Vector2 anchor;
        public readonly Vector2 position;
        public readonly Vector2 size;

        public HudPanel(string panelName, Vector2 panelAnchor, Vector2 panelPosition, Vector2 panelSize)
        {
            name = panelName;
            anchor = panelAnchor;
            position = panelPosition;
            size = panelSize;
        }
    }
}
