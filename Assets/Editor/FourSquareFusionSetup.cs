#if UNITY_EDITOR
using System;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

public static class FourSquareFusionSetup
{
    private const string CityScenePath = "Assets/Scenes/City.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/VR Player.prefab";
    private const string BallPrefabPath = "Assets/Prefabs/Ball.prefab";
    private const string HandMarkerMaterialPath = "Assets/Materials/Light_Grey.mat";

    [MenuItem("FourSquare/Configure Fusion Gameplay")]
    public static void Configure()
    {
        ConfigurePlayerPrefab();
        ConfigureBallPrefab();
        ConfigureCityScene();
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();
        Debug.Log("Fusion gameplay is configured for the City scene and the VR Player/Ball prefabs.");
    }

    private static void ConfigurePlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            XROrigin origin = root.GetComponent<XROrigin>();
            if (origin == null) throw new InvalidOperationException("VR Player prefab needs an XROrigin on its root.");

            Transform leftHand = FindChild(root.transform, "Left Hand");
            Transform rightHand = FindChild(root.transform, "Right Hand");
            if (leftHand == null || rightHand == null)
            {
                throw new InvalidOperationException("VR Player prefab needs child transforms named Left Hand and Right Hand.");
            }

            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
            FourSquareFusionPlayer player = root.GetComponent<FourSquareFusionPlayer>();
            if (player == null) player = root.AddComponent<FourSquareFusionPlayer>();

            SerializedObject serializedPlayer = new SerializedObject(player);
            serializedPlayer.FindProperty("xrOrigin").objectReferenceValue = origin;
            serializedPlayer.FindProperty("head").objectReferenceValue = origin.Camera != null ? origin.Camera.transform : null;
            serializedPlayer.FindProperty("leftHand").objectReferenceValue = leftHand;
            serializedPlayer.FindProperty("rightHand").objectReferenceValue = rightHand;
            SerializedProperty renderers = serializedPlayer.FindProperty("avatarRenderers");
            renderers.arraySize = root.GetComponentsInChildren<Renderer>(true).Length;
            Renderer[] childRenderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < childRenderers.Length; i++)
            {
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = childRenderers[i];
            }
            serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

            AddHandMarker(leftHand);
            AddHandMarker(rightHand);
            new NetworkObjectBaker().Bake(root);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureBallPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(BallPrefabPath);
        try
        {
            if (root.GetComponent<NetworkBallController>() == null)
            {
                throw new InvalidOperationException("Ball prefab needs a NetworkBallController on its root.");
            }
            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
            if (root.GetComponent<FoursquareBall>() == null) root.AddComponent<FoursquareBall>();

            new NetworkObjectBaker().Bake(root);
            PrefabUtility.SaveAsPrefabAsset(root, BallPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureCityScene()
    {
        Scene previousActiveScene = SceneManager.GetActiveScene();
        Scene cityScene = FindLoadedScene(CityScenePath);
        bool openedScene = !cityScene.IsValid();
        if (openedScene) cityScene = EditorSceneManager.OpenScene(CityScenePath, OpenSceneMode.Additive);

        try
        {
            RemoveScenePlayerRigs(cityScene);

            GameObject managerObject = FindGameStateManager(cityScene);
            if (managerObject == null)
            {
                GameObject courtObject = FindGameObject(cityScene, "Game Court");
                if (courtObject == null)
                {
                    throw new InvalidOperationException("City scene needs a Game Court object before Fusion can be configured.");
                }

                managerObject = courtObject;
            }

            GameStateManager gameStateManager = managerObject.GetComponent<GameStateManager>();
            if (gameStateManager == null) gameStateManager = managerObject.AddComponent<GameStateManager>();
            GameObject ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
            SerializedObject serializedManager = new SerializedObject(gameStateManager);
            serializedManager.FindProperty("ballPrefab").objectReferenceValue = ballPrefab;

            Transform ballSpawnPoint = FindTransform(cityScene, "Ball Spawn Point");
            serializedManager.FindProperty("ballSpawnPoint").objectReferenceValue = ballSpawnPoint;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();

            ConfigureCityRuntime(cityScene, managerObject, ballPrefab);

            FourSquareFusionSession session = managerObject.GetComponent<FourSquareFusionSession>();
            if (session == null) session = managerObject.AddComponent<FourSquareFusionSession>();

            SerializedObject serializedSession = new SerializedObject(session);
            serializedSession.FindProperty("sessionName").stringValue = "FourSquareVR";
            serializedSession.FindProperty("maximumPlayers").intValue = 4;
            serializedSession.FindProperty("connectOnStart").boolValue = true;
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            serializedSession.FindProperty("playerPrefab").objectReferenceValue =
                playerPrefab != null ? playerPrefab.GetComponent<NetworkObject>() : null;
            Transform playerSpawnPoint = GetOrCreatePlayerSpawnPoint(cityScene, managerObject.transform);
            serializedSession.FindProperty("playerSpawnPoint").objectReferenceValue = playerSpawnPoint;
            serializedSession.ApplyModifiedPropertiesWithoutUndo();

            if (ballPrefab == null)
            {
                throw new InvalidOperationException("Ball prefab was not found at " + BallPrefabPath + ".");
            }
            if (playerPrefab == null || playerPrefab.GetComponent<NetworkObject>() == null)
            {
                throw new InvalidOperationException("The VR Player prefab needs a baked NetworkObject before Fusion can start.");
            }

            EditorSceneManager.MarkSceneDirty(cityScene);
            EditorSceneManager.SaveScene(cityScene);
        }
        finally
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }
            if (openedScene) EditorSceneManager.CloseScene(cityScene, true);
        }
    }

    public static void ConfigureCityRuntime(Scene cityScene, GameObject managerObject, GameObject ballPrefab)
    {
        PlayerJoinCourt joinCourt = FindComponent<PlayerJoinCourt>(cityScene);
        if (joinCourt == null)
            throw new InvalidOperationException("City scene needs a PlayerJoinCourt component.");

        SerializedObject serializedJoinCourt = new SerializedObject(joinCourt);
        BoxCollider courtBounds =
            serializedJoinCourt.FindProperty("courtBounds").objectReferenceValue as BoxCollider;
        SerializedProperty spawnPoints = serializedJoinCourt.FindProperty("squareSpawnPoints");
        if (courtBounds == null || spawnPoints.arraySize != FoursquareCourt.SquareCount)
            throw new InvalidOperationException(
                "PlayerJoinCourt needs court bounds and four spawn points ordered Square 1 through Square 4.");

        Transform[] squareSpawnPoints = new Transform[FoursquareCourt.SquareCount];
        for (int i = 0; i < squareSpawnPoints.Length; i++)
        {
            squareSpawnPoints[i] =
                spawnPoints.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (squareSpawnPoints[i] == null)
                throw new InvalidOperationException($"PlayerJoinCourt is missing the Square {i + 1} spawn point.");
        }

        Transform boundaryTransform = FindTransform(cityScene, "Ball Out Of Bounds Bounds");
        if (boundaryTransform == null)
        {
            GameObject boundaryObject = new GameObject("Ball Out Of Bounds Bounds");
            boundaryTransform = boundaryObject.transform;
            boundaryTransform.SetParent(courtBounds.transform.parent, false);
        }

        boundaryTransform.localPosition = courtBounds.transform.localPosition;
        boundaryTransform.localRotation = courtBounds.transform.localRotation;
        boundaryTransform.localScale = courtBounds.transform.localScale;
        BoxCollider boundaryCollider = boundaryTransform.GetComponent<BoxCollider>();
        if (boundaryCollider == null)
            boundaryCollider = boundaryTransform.gameObject.AddComponent<BoxCollider>();
        boundaryCollider.center = courtBounds.center;
        boundaryCollider.size = new Vector3(courtBounds.size.x, 100f, courtBounds.size.z);
        boundaryCollider.isTrigger = true;

        FoursquareCourt court = boundaryTransform.GetComponent<FoursquareCourt>();
        if (court == null)
            court = boundaryTransform.gameObject.AddComponent<FoursquareCourt>();
        SerializedObject serializedCourt = new SerializedObject(court);
        serializedCourt.FindProperty("courtBounds").objectReferenceValue = courtBounds;
        SerializedProperty serializedSpawns = serializedCourt.FindProperty("squareSpawnPoints");
        serializedSpawns.arraySize = squareSpawnPoints.Length;
        for (int i = 0; i < squareSpawnPoints.Length; i++)
            serializedSpawns.GetArrayElementAtIndex(i).objectReferenceValue = squareSpawnPoints[i];
        serializedCourt.FindProperty("kingServePoint").objectReferenceValue = null;
        serializedCourt.ApplyModifiedPropertiesWithoutUndo();

        FoursquareGameManager gameManager = managerObject.GetComponent<FoursquareGameManager>();
        if (gameManager == null)
            gameManager = managerObject.AddComponent<FoursquareGameManager>();

        GameObject testBall = FindGameObject(cityScene, "Test Ball");
        if (testBall == null)
            throw new InvalidOperationException("City scene is missing the existing Test Ball GameObject.");

        BallTrajectoryController trajectory = testBall.GetComponent<BallTrajectoryController>();
        Vector3 fallbackPosition = trajectory != null
            ? new SerializedObject(trajectory).FindProperty("initialDropPosition").vector3Value
            : testBall.transform.position;
        if (trajectory != null)
            UnityEngine.Object.DestroyImmediate(trajectory, true);

        BallController legacyController = testBall.GetComponent<BallController>();
        if (legacyController != null)
            UnityEngine.Object.DestroyImmediate(legacyController, true);

        NetworkObject networkObject = testBall.GetComponent<NetworkObject>();
        if (networkObject == null)
            networkObject = testBall.AddComponent<NetworkObject>();
        NetworkTransform networkTransform = testBall.GetComponent<NetworkTransform>();
        if (networkTransform == null)
            networkTransform = testBall.AddComponent<NetworkTransform>();
        NetworkBallController networkBall = testBall.GetComponent<NetworkBallController>();
        if (networkBall == null)
            networkBall = testBall.AddComponent<NetworkBallController>();
        FoursquareBall rules = testBall.GetComponent<FoursquareBall>();
        if (rules == null)
            rules = testBall.AddComponent<FoursquareBall>();

        SerializedObject serializedNetworkBall = new SerializedObject(networkBall);
        serializedNetworkBall.FindProperty("courtTargetManager").objectReferenceValue =
            managerObject.GetComponent<CourtTargetManager>();
        serializedNetworkBall.FindProperty("ballRigidbody").objectReferenceValue =
            testBall.GetComponent<Rigidbody>();
        serializedNetworkBall.FindProperty("networkTransform").objectReferenceValue = networkTransform;
        serializedNetworkBall.FindProperty("fallbackDropPosition").vector3Value = fallbackPosition;
        serializedNetworkBall.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedRules = new SerializedObject(rules);
        serializedRules.FindProperty("networkBall").objectReferenceValue = networkBall;
        serializedRules.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject serializedGameManager = new SerializedObject(gameManager);
        serializedGameManager.FindProperty("gameStateManager").objectReferenceValue =
            managerObject.GetComponent<GameStateManager>();
        serializedGameManager.FindProperty("court").objectReferenceValue = court;
        serializedGameManager.FindProperty("ball").objectReferenceValue = rules;
        serializedGameManager.FindProperty("networkBall").objectReferenceValue = networkBall;
        serializedGameManager.ApplyModifiedPropertiesWithoutUndo();

        new NetworkObjectBaker().Bake(testBall);
        EditorSceneManager.MarkSceneDirty(cityScene);
    }

    private static T FindComponent<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }

    private static Transform GetOrCreatePlayerSpawnPoint(Scene scene, Transform sessionTransform)
    {
        Transform spawnPoint = FindTransform(scene, "Player Spawn Point");
        if (spawnPoint != null) return spawnPoint;

        Transform joinZone = FindTransform(scene, "Join Zone Area");
        Vector3 position = joinZone != null ? joinZone.position : sessionTransform.position;
        Vector3 facing = sessionTransform.position - position;
        facing.y = 0f;
        Quaternion rotation = facing.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(facing.normalized, Vector3.up)
            : sessionTransform.rotation;

        GameObject marker = new GameObject("Player Spawn Point");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(marker, scene);
        marker.transform.SetPositionAndRotation(position, rotation);
        Debug.Log(
            $"Created Player Spawn Point in '{scene.name}'. Move this marker in the Scene view to choose where Fusion-spawned players appear.",
            marker);
        return marker.transform;
    }

    private static void RemoveScenePlayerRigs(Scene scene)
    {
        int removedPlayers = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "VR Player" && root.GetComponent<XROrigin>() != null)
            {
                string playerName = root.name;
                UnityEngine.Object.DestroyImmediate(root);
                removedPlayers++;
                Debug.LogWarning(
                    $"Removed the scene-placed '{playerName}' XR rig from scene '{scene.name}'. " +
                    "The Fusion session spawns the configured VR Player prefab, so keeping both rigs can interrupt XR tracking.");
                continue;
            }

            foreach (XROrigin origin in root.GetComponentsInChildren<XROrigin>(true))
            {
                FourSquareFusionPlayer scenePlayer = origin.GetComponent<FourSquareFusionPlayer>();
                if (scenePlayer == null) continue;

                NetworkObject sceneNetworkObject = origin.GetComponent<NetworkObject>();
                UnityEngine.Object.DestroyImmediate(scenePlayer, true);
                if (sceneNetworkObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(sceneNetworkObject, true);
                }

                removedPlayers++;
                Debug.LogWarning(
                    $"Removed scene-level Fusion player from '{origin.name}'. Fusion players should only come from the configured VR Player prefab.",
                    origin);
            }
        }

        if (removedPlayers > 0)
        {
            Debug.Log($"Removed {removedPlayers} scene-level Fusion player object(s) from {scene.name}.");
        }
    }

    private static void AddHandMarker(Transform hand)
    {
        BallHandMarker marker = hand.GetComponent<BallHandMarker>();
        if (marker == null) marker = hand.gameObject.AddComponent<BallHandMarker>();

        Material material = AssetDatabase.LoadAssetAtPath<Material>(HandMarkerMaterialPath);
        if (material == null)
        {
            throw new InvalidOperationException(
                $"Required hand marker material was not found at '{HandMarkerMaterialPath}'.");
        }

        SerializedObject serializedMarker = new SerializedObject(marker);
        serializedMarker.FindProperty("visualMaterial").objectReferenceValue = material;
        serializedMarker.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName) return child;
        }

        return null;
    }

    private static GameObject FindGameStateManager(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameStateManager manager = root.GetComponentInChildren<GameStateManager>(true);
            if (manager != null) return manager.gameObject;
        }

        return null;
    }

    private static GameObject FindGameObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName) return child.gameObject;
            }
        }

        return null;
    }

    private static Transform FindTransform(Scene scene, string objectName)
    {
        GameObject found = FindGameObject(scene, objectName);
        return found != null ? found.transform : null;
    }

    private static Scene FindLoadedScene(string path)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.path == path) return scene;
        }

        return default;
    }
}
#endif
