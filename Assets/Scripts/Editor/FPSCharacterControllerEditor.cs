using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FPSCharacterController))]
public class FPSCharacterControllerEditor : Editor
{
    const int None = -1;
    const int Forward = 0;
    const int Backward = 1;
    const int Left = 2;
    const int Right = 3;
    const int Jump = 4;
    const int Crouch = 5;
    const int Sprint = 6;
    const int Blast = 7;

    int _pendingBind = None;

    static readonly string[] ExcludedKeyFields =
    {
        "forwardKey", "backwardKey", "leftKey", "rightKey", "jumpKey", "crouchKey", "sprintKey", "blastKey"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, ExcludedKeyFields);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Key bindings", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Click Bind, then press the key (or mouse button) to assign. Escape cancels.", MessageType.Info);

        var forward = serializedObject.FindProperty("forwardKey");
        var backward = serializedObject.FindProperty("backwardKey");
        var left = serializedObject.FindProperty("leftKey");
        var right = serializedObject.FindProperty("rightKey");
        var jump = serializedObject.FindProperty("jumpKey");
        var crouch = serializedObject.FindProperty("crouchKey");
        var sprint = serializedObject.FindProperty("sprintKey");
        var blast = serializedObject.FindProperty("blastKey");

        DrawBindRow("Forward", Forward, forward);
        DrawBindRow("Backward", Backward, backward);
        DrawBindRow("Left", Left, left);
        DrawBindRow("Right", Right, right);
        DrawBindRow("Jump", Jump, jump);
        DrawBindRow("Crouch", Crouch, crouch);
        DrawBindRow("Sprint", Sprint, sprint);
        DrawBindRow("Blast", Blast, blast);

        HandlePendingBind();

        serializedObject.ApplyModifiedProperties();
    }

    void DrawBindRow(string label, int bindId, SerializedProperty keyProp)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(label);

        var current = (KeyCode)keyProp.intValue;

        using (new EditorGUI.DisabledScope(_pendingBind != None && _pendingBind != bindId))
            EditorGUILayout.LabelField(current.ToString(), GUILayout.MinWidth(96f));

        if (_pendingBind == bindId)
        {
            GUI.backgroundColor = new Color(0.5f, 0.85f, 0.55f);
            GUILayout.Button("Listening…", GUILayout.Width(100f));
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Cancel", GUILayout.Width(56f)))
                _pendingBind = None;
        }
        else
        {
            using (new EditorGUI.DisabledScope(_pendingBind != None))
            {
                if (GUILayout.Button("Bind", GUILayout.Width(56f)))
                {
                    int id = bindId;
                    EditorApplication.delayCall += () => _pendingBind = id;
                }
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    void HandlePendingBind()
    {
        if (_pendingBind == None)
            return;

        var e = Event.current;
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape)
            {
                _pendingBind = None;
                e.Use();
                Repaint();
                return;
            }

            if (e.keyCode != KeyCode.None)
            {
                ApplyKey(_pendingBind, e.keyCode);
                _pendingBind = None;
                e.Use();
                Repaint();
            }
            return;
        }

        if (e.type == EventType.MouseDown && e.button >= 0 && e.button <= 6)
        {
            ApplyKey(_pendingBind, KeyCode.Mouse0 + e.button);
            _pendingBind = None;
            e.Use();
            Repaint();
        }
    }

    void ApplyKey(int bindId, KeyCode key)
    {
        string path = bindId switch
        {
            Forward => "forwardKey",
            Backward => "backwardKey",
            Left => "leftKey",
            Right => "rightKey",
            Jump => "jumpKey",
            Crouch => "crouchKey",
            Sprint => "sprintKey",
            Blast => "blastKey",
            _ => null
        };

        if (path == null)
            return;

        var prop = serializedObject.FindProperty(path);
        Undo.RecordObject(target, "Rebind FPS input");
        prop.intValue = (int)key;
        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
    }
}
