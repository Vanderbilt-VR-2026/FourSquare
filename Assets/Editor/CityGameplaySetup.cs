#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CityGameplaySetup
{
    private const string ScenePath = "Assets/Scenes/City.unity";
    private const string BallPrefabPath = "Assets/Prefabs/Ball.prefab";

    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject court = GameObject.Find("Game Court");
        GameObject joinZone = GameObject.Find("Join Zone Area");
        if (court == null || joinZone == null)
        {
            throw new InvalidOperationException("City scene is missing Game Court or Join Zone Area.");
        }

        Transform courtTransform = court.transform;
        Bounds courtBounds = CalculateCourtBounds(courtTransform);
        float floorY = courtBounds.min.y;
        float centerX = (courtBounds.min.x + courtBounds.max.x) * 0.5f;
        float centerZ = (courtBounds.min.z + courtBounds.max.z) * 0.5f;

        Transform boundsTransform = GetOrCreateChild(courtTransform, "Court Bounds");
        boundsTransform.localPosition = new Vector3(centerX, floorY + 2f, centerZ);
        boundsTransform.localRotation = Quaternion.identity;
        boundsTransform.localScale = Vector3.one;
        BoxCollider boundsCollider = boundsTransform.GetComponent<BoxCollider>();
        if (boundsCollider == null) boundsCollider = boundsTransform.gameObject.AddComponent<BoxCollider>();
        boundsCollider.center = Vector3.zero;
        boundsCollider.size = new Vector3(courtBounds.size.x, 4f, courtBounds.size.z);
        boundsCollider.isTrigger = true;

        Transform exitPoint = GetOrCreateChild(courtTransform, "Court Exit Point");
        Vector3 joinLocalPosition = courtTransform.InverseTransformPoint(joinZone.transform.position);
        Vector3 boundsLocalCenter = boundsTransform.localPosition;
        Vector3 halfSize = boundsCollider.size * 0.5f;
        float distanceToMinX = Mathf.Abs(joinLocalPosition.x - (boundsLocalCenter.x - halfSize.x));
        float distanceToMaxX = Mathf.Abs(joinLocalPosition.x - (boundsLocalCenter.x + halfSize.x));
        float distanceToMinZ = Mathf.Abs(joinLocalPosition.z - (boundsLocalCenter.z - halfSize.z));
        float distanceToMaxZ = Mathf.Abs(joinLocalPosition.z - (boundsLocalCenter.z + halfSize.z));
        float nearestEdge = Mathf.Min(distanceToMinX, distanceToMaxX, distanceToMinZ, distanceToMaxZ);
        Vector3 exitLocalPosition = new Vector3(centerX, floorY, centerZ);
        if (nearestEdge == distanceToMinX) exitLocalPosition.x = boundsLocalCenter.x - halfSize.x - 1f;
        else if (nearestEdge == distanceToMaxX) exitLocalPosition.x = boundsLocalCenter.x + halfSize.x + 1f;
        else if (nearestEdge == distanceToMinZ) exitLocalPosition.z = boundsLocalCenter.z - halfSize.z - 1f;
        else exitLocalPosition.z = boundsLocalCenter.z + halfSize.z + 1f;
        exitPoint.localPosition = exitLocalPosition;
        exitPoint.localRotation = Quaternion.identity;
        exitPoint.localScale = Vector3.one;

        PlayerJoinCourt joinCourt = joinZone.GetComponent<PlayerJoinCourt>();
        if (joinCourt == null) joinCourt = joinZone.AddComponent<PlayerJoinCourt>();
        SerializedObject joinSerialized = new SerializedObject(joinCourt);
        joinSerialized.FindProperty("courtBounds").objectReferenceValue = boundsCollider;
        joinSerialized.FindProperty("exitPoint").objectReferenceValue = exitPoint;
        joinSerialized.FindProperty("minimumPlayersToStart").intValue = 1;
        joinSerialized.FindProperty("maximumPlayers").intValue = 4;
        joinSerialized.ApplyModifiedPropertiesWithoutUndo();

        for (int squareId = 1; squareId <= 4; squareId++)
        {
            GameObject square = GameObject.Find("Square_" + squareId);
            if (square == null) throw new InvalidOperationException("City scene is missing Square_" + squareId + ".");
            CourtSquare courtSquare = square.GetComponent<CourtSquare>();
            if (courtSquare == null) courtSquare = square.AddComponent<CourtSquare>();
            courtSquare.squareID = squareId;
        }

        AddLineFault("Inline_1");
        AddLineFault("Inline_2");

        Transform ballSpawn = GetOrCreateChild(courtTransform, "Ball Spawn Point");
        ballSpawn.localPosition = new Vector3(centerX, floorY + 1.5f, centerZ);
        ballSpawn.localRotation = Quaternion.identity;
        ballSpawn.localScale = Vector3.one;

        GameStateManager manager = court.GetComponent<GameStateManager>();
        if (manager == null) manager = court.AddComponent<GameStateManager>();
        GameObject ballPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
        if (ballPrefab == null) throw new InvalidOperationException("Ball prefab was not found at " + BallPrefabPath + ".");

        SerializedObject managerSerialized = new SerializedObject(manager);
        managerSerialized.FindProperty("ballPrefab").objectReferenceValue = ballPrefab;
        managerSerialized.FindProperty("ballSpawnPoint").objectReferenceValue = ballSpawn;
        SerializedProperty players = managerSerialized.FindProperty("playerList");
        players.arraySize = 1;
        SerializedProperty player = players.GetArrayElementAtIndex(0);
        player.FindPropertyRelative("playerId").stringValue = "local-debug-player";
        player.FindPropertyRelative("playerName").stringValue = "Debug Player";
        player.FindPropertyRelative("currentRole").enumValueIndex = 3;
        player.FindPropertyRelative("score").intValue = 0;
        player.FindPropertyRelative("eliminationsCount").intValue = 0;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("City gameplay setup completed: manager, ball spawn, bounds, join references, square IDs, and line faults.");
    }

    private static Bounds CalculateCourtBounds(Transform courtTransform)
    {
        bool hasBounds = false;
        Bounds bounds = new Bounds();
        for (int squareId = 1; squareId <= 4; squareId++)
        {
            GameObject square = GameObject.Find("Square_" + squareId);
            Collider squareCollider = square != null ? square.GetComponent<Collider>() : null;
            if (squareCollider == null) throw new InvalidOperationException("Square_" + squareId + " needs a collider.");

            Bounds worldBounds = squareCollider.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = new Vector3(
                    (corner & 1) == 0 ? worldBounds.min.x : worldBounds.max.x,
                    (corner & 2) == 0 ? worldBounds.min.y : worldBounds.max.y,
                    (corner & 4) == 0 ? worldBounds.min.z : worldBounds.max.z);
                point = courtTransform.InverseTransformPoint(point);
                if (!hasBounds)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }

        return bounds;
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null) return child;

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    private static void AddLineFault(string objectName)
    {
        GameObject line = GameObject.Find(objectName);
        if (line == null) throw new InvalidOperationException("City scene is missing " + objectName + ".");
        if (line.GetComponent<Collider>() == null) throw new InvalidOperationException(objectName + " needs a collider.");
        if (line.GetComponent<CourtLineFault>() == null) line.AddComponent<CourtLineFault>();
    }
}
#endif
