using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Prefab Replacer
/// Finds every scene object whose name matches "Search Name" and replaces it
/// with an instance of "New Prefab", preserving world position / rotation / scale.
/// Fully undoable (Edit > Undo Prefab Replace).
/// </summary>
public class PrefabReplacerWindow : EditorWindow
{
    [System.Serializable]
    private class ReplacePair
    {
        public string     searchName = "";
        public GameObject newPrefab;
        public bool       onlyPrefabInstances = true;
        public bool       startsWith = true;   // false = exact match
    }

    private readonly List<ReplacePair> _pairs = new()
    {
        new ReplacePair(),
        new ReplacePair(),
    };

    private Vector2 _scroll;
    private string  _lastResult = "";

    [MenuItem("Tools/Prefab Replacer")]
    public static void Open() => GetWindow<PrefabReplacerWindow>("Prefab Replacer");

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Prefab Replacer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Search Name: the exact GameObject name to find in the scene.\n" +
            "← Sel: auto-fills the name from your Hierarchy selection.\n" +
            "New Prefab: drag from the Project window.\n" +
            "Fully undoable.",
            MessageType.Info);

        EditorGUILayout.Space(4);

        // column headers
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(54);
        EditorGUILayout.LabelField("Search Name", EditorStyles.miniLabel, GUILayout.Width(160));
        EditorGUILayout.LabelField("New Prefab", EditorStyles.miniLabel, GUILayout.Width(160));
        EditorGUILayout.LabelField("StartsWith", EditorStyles.miniLabel, GUILayout.Width(62));
        EditorGUILayout.LabelField("Prefab only", EditorStyles.miniLabel, GUILayout.Width(72));
        EditorGUILayout.EndHorizontal();

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(false), GUILayout.MaxHeight(220));

        for (int i = 0; i < _pairs.Count; i++)
        {
            var pair = _pairs[i];
            EditorGUILayout.BeginHorizontal();

            // ← Sel: fill name from selection
            if (GUILayout.Button("← Sel", GUILayout.Width(50)))
            {
                var sel = Selection.activeGameObject;
                if (sel != null)
                {
                    pair.searchName = sel.name;
                    Repaint();
                }
                else
                    Debug.LogWarning("[PrefabReplacer] Nothing selected in the Hierarchy.");
            }

            pair.searchName = EditorGUILayout.TextField(pair.searchName, GUILayout.Width(160));

            pair.newPrefab = (GameObject)EditorGUILayout.ObjectField(
                pair.newPrefab, typeof(GameObject), false, GUILayout.Width(160));

            pair.startsWith = EditorGUILayout.Toggle(pair.startsWith, GUILayout.Width(20));

            pair.onlyPrefabInstances = EditorGUILayout.Toggle(
                pair.onlyPrefabInstances, GUILayout.Width(20));

            if (GUILayout.Button("✕", GUILayout.Width(24)) && _pairs.Count > 1)
                _pairs.RemoveAt(i--);

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.HelpBox(
            "StartsWith ON: matches \"buildingMedium\", \"buildingMedium (1)\", \"buildingMedium (2)\", etc.\n" +
            "StartsWith OFF: exact name match only.\n" +
            "Prefab only: skip plain GameObjects; uncheck if prefab links are fully broken.",
            MessageType.None);

        if (GUILayout.Button("+ Add Row"))
            _pairs.Add(new ReplacePair());

        EditorGUILayout.Space(6);

        bool canReplace = _pairs.Exists(p => !string.IsNullOrWhiteSpace(p.searchName) && p.newPrefab);
        GUI.enabled = canReplace;
        if (GUILayout.Button("Replace All Instances in Open Scenes", GUILayout.Height(32)))
            DoReplace();
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(_lastResult))
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_lastResult, MessageType.None);
        }
    }

    private void DoReplace()
    {
        var summary = new System.Text.StringBuilder();
        Undo.SetCurrentGroupName("Prefab Replace");
        int undoGroup = Undo.GetCurrentGroup();

        foreach (var pair in _pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.searchName) || !pair.newPrefab) continue;
            int count = ReplaceByName(pair.searchName, pair.newPrefab, pair.onlyPrefabInstances, pair.startsWith);
            summary.AppendLine($"\"{pair.searchName}\"  →  {pair.newPrefab.name} : {count} replaced");
        }

        Undo.CollapseUndoOperations(undoGroup);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isLoaded) EditorSceneManager.MarkSceneDirty(s);
        }

        _lastResult = summary.Length > 0 ? summary.ToString().TrimEnd() : "Nothing replaced.";
        Debug.Log("[PrefabReplacer] " + _lastResult);
    }

    private static int ReplaceByName(
        string searchName, GameObject newPrefab, bool onlyPrefabInstances, bool startsWith)
    {
        var toReplace = new List<GameObject>();

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (var root in scene.GetRootGameObjects())
                CollectByName(root, searchName, onlyPrefabInstances, startsWith, toReplace);
        }

        foreach (var go in toReplace)
            ReplaceOne(go, newPrefab);

        return toReplace.Count;
    }

    private static void CollectByName(
        GameObject go, string searchName, bool onlyPrefabInstances,
        bool startsWith, List<GameObject> results)
    {
        bool nameMatches = startsWith
            ? go.name.StartsWith(searchName)
            : go.name == searchName;

        if (nameMatches)
        {
            bool isPrefabInstance = PrefabUtility.GetPrefabInstanceStatus(go)
                                    != PrefabInstanceStatus.NotAPrefab;

            if (!onlyPrefabInstances || isPrefabInstance)
            {
                results.Add(go);
                return; // don't recurse into matched object's children
            }
        }

        foreach (Transform child in go.transform)
            CollectByName(child.gameObject, searchName, onlyPrefabInstances, startsWith, results);
    }

    private static void ReplaceOne(GameObject oldGo, GameObject newPrefab)
    {
        var t = oldGo.transform;

        var newGo = (GameObject)PrefabUtility.InstantiatePrefab(newPrefab, oldGo.scene);
        Undo.RegisterCreatedObjectUndo(newGo, "Prefab Replace");

        var newT = newGo.transform;
        Undo.SetTransformParent(newT, t.parent, "Prefab Replace");

        newT.SetPositionAndRotation(t.position, t.rotation);
        newT.localScale = t.localScale;
        newT.SetSiblingIndex(t.GetSiblingIndex());
        newGo.name = oldGo.name;

        Undo.DestroyObjectImmediate(oldGo);
    }
}
