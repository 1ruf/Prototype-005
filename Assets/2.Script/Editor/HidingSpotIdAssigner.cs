#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Assigns serialized, unique hiding spot IDs to the currently open scenes.
/// Run after duplicating or placing lockers so clients and the host resolve the same spot.
/// </summary>
public static class HidingSpotIdAssigner
{
    [MenuItem("Tools/Prototype005/Hiding/Assign Unique IDs In Open Scenes")]
    public static void AssignUniqueIdsInOpenScenes()
    {
        List<NetworkHidingSpot> spots = Object.FindObjectsByType<NetworkHidingSpot>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .Where(spot => spot != null && spot.gameObject.scene.IsValid() && spot.gameObject.scene.isLoaded)
            .OrderBy(spot => spot.gameObject.scene.path)
            .ThenBy(spot => GetHierarchyPath(spot.transform))
            .ToList();

        if (spots.Count == 0)
        {
            Debug.Log("HidingSpotIdAssigner: no hiding spots were found in the open scenes.");
            return;
        }

        Dictionary<int, int> idCounts = new Dictionary<int, int>();
        int nextId = 1;
        foreach (NetworkHidingSpot spot in spots)
        {
            int id = GetSpotId(spot);
            if (id > 0)
            {
                idCounts.TryGetValue(id, out int count);
                idCounts[id] = count + 1;
                nextId = Mathf.Max(nextId, id + 1);
            }
        }

        int changedCount = 0;
        foreach (NetworkHidingSpot spot in spots)
        {
            int id = GetSpotId(spot);
            bool needsId = id <= 0 || (idCounts.TryGetValue(id, out int count) && count > 1);
            if (!needsId)
                continue;

            while (idCounts.ContainsKey(nextId))
                nextId++;

            Undo.RecordObject(spot, "Assign Unique Hiding Spot ID");
            SerializedObject serializedSpot = new SerializedObject(spot);
            serializedSpot.FindProperty("spotId").intValue = nextId;
            serializedSpot.ApplyModifiedProperties();
            EditorUtility.SetDirty(spot);
            EditorSceneManager.MarkSceneDirty(spot.gameObject.scene);

            idCounts[nextId] = 1;
            nextId++;
            changedCount++;
        }

        Debug.Log($"HidingSpotIdAssigner: assigned {changedCount} unique hiding spot ID(s).");
    }

    private static int GetSpotId(NetworkHidingSpot spot)
    {
        SerializedObject serializedSpot = new SerializedObject(spot);
        SerializedProperty spotId = serializedSpot.FindProperty("spotId");
        return spotId != null ? spotId.intValue : 0;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        for (Transform current = transform.parent; current != null; current = current.parent)
            path = current.name + "/" + path;

        return path;
    }
}
#endif
