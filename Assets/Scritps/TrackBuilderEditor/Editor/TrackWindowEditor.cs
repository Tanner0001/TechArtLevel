// TrackBuilderWindow.cs
// Place in Assets/Editor

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Splines;

public class TrackBuilderWindow : EditorWindow
{
    // -------- Source --------
    [SerializeField] SplineContainer spline;
    [SerializeField] Transform parent;

    // Fallback single prefab (optional if you don't use the Modules list)
    [SerializeField] GameObject fallbackPrefab;

    // -------- Placement --------
    [SerializeField] bool alignToTangent = true;
    [SerializeField] bool autoSpacingFromPrefab = true;
    [SerializeField] float spacing = 5f;               // used only if autoSpacingFromPrefab == false
    [SerializeField] float startOffset = 0f;
    [SerializeField] float endOffset = 0f;

    // Orientation tweaks
    [SerializeField] float yawOffsetDeg = 0f;          // around up
    [SerializeField] float rollOffsetDeg = 0f;         // around forward
    [SerializeField] Vector3 localPositionOffset = Vector3.zero;

    // -------- Quality --------
    [SerializeField] bool autoQuality = true;
    [SerializeField] int lengthSteps = 512;            // for length approximation
    [SerializeField] int mapSteps = 2048;              // for distance->t mapping

    // -------- Modules (weighted random) --------
    [System.Serializable]
    class Module
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float weight = 1f;
    }

    [SerializeField] List<Module> modules = new List<Module>();
    ReorderableList moduleList;

    // -------- UI & state --------
    Vector2 scroll;
    string _error, _warning, _info;

    [MenuItem("Tools/RaceTrack Builder")]
    public static void Open() => GetWindow<TrackBuilderWindow>("RaceTrack Builder").Show();

    void OnEnable()
    {
        if (modules == null) modules = new List<Module>();
        moduleList = new ReorderableList(modules, typeof(Module), true, true, true, true);
        moduleList.drawHeaderCallback = rect => GUI.Label(rect, "Module Set (weighted random)");
        moduleList.elementHeight = EditorGUIUtility.singleLineHeight * 3f + 6f;
        moduleList.drawElementCallback = (rect, index, active, focused) =>
        {
            var m = modules[index];
            var line = new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight);
            m.prefab = (GameObject)EditorGUI.ObjectField(line, "Prefab", m.prefab, typeof(GameObject), false);
            line.y += EditorGUIUtility.singleLineHeight + 2;
            m.weight = EditorGUI.Slider(line, "Weight", m.weight, 0f, 1f);
        };
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
        spline = (SplineContainer)EditorGUILayout.ObjectField("Spline", spline, typeof(SplineContainer), true);
        parent = (Transform)EditorGUILayout.ObjectField("Parent (optional)", parent, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
        fallbackPrefab = (GameObject)EditorGUILayout.ObjectField("Fallback Prefab", fallbackPrefab, typeof(GameObject), false);
        moduleList.DoLayoutList();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        alignToTangent = EditorGUILayout.Toggle("Align to Tangent", alignToTangent);
        autoSpacingFromPrefab = EditorGUILayout.Toggle("Auto Spacing From Prefab", autoSpacingFromPrefab);
        using (new EditorGUI.DisabledScope(autoSpacingFromPrefab))
        {
            spacing = Mathf.Max(0.01f, EditorGUILayout.FloatField("Spacing (m)", spacing));
        }
        startOffset = Mathf.Max(0f, EditorGUILayout.FloatField("Start Offset (m)", startOffset));
        endOffset = Mathf.Max(0f, EditorGUILayout.FloatField("End Offset (m)", endOffset));

        yawOffsetDeg = EditorGUILayout.Slider("Yaw Offset (deg)", yawOffsetDeg, -180f, 180f);
        rollOffsetDeg = EditorGUILayout.Slider("Roll Offset (deg)", rollOffsetDeg, -90f, 90f);
        localPositionOffset = EditorGUILayout.Vector3Field("Local Pos Offset", localPositionOffset);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quality", EditorStyles.boldLabel);
        autoQuality = EditorGUILayout.Toggle("Auto Quality", autoQuality);
        using (new EditorGUI.DisabledScope(autoQuality))
        {
            lengthSteps = Mathf.Clamp(EditorGUILayout.IntField("Length Steps", lengthSteps), 64, 8192);
            mapSteps = Mathf.Clamp(EditorGUILayout.IntField("Map Steps", mapSteps), 128, 16384);
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = CanGenerate();
            if (GUILayout.Button("Generate", GUILayout.Height(32)))
                Generate();

            GUI.enabled = parent != null;
            if (GUILayout.Button("Clear Parent Children", GUILayout.Height(32)))
                ClearParentChildren();

            if (GUILayout.Button("Select Parent", GUILayout.Height(32)))
                Selection.activeTransform = parent;

            GUI.enabled = true;
        }

        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(_error))
            EditorGUILayout.HelpBox(_error, MessageType.Error);
        else if (!string.IsNullOrEmpty(_warning))
            EditorGUILayout.HelpBox(_warning, MessageType.Warning);
        else if (!string.IsNullOrEmpty(_info))
            EditorGUILayout.HelpBox(_info, MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Tips:\n" +
            "• For perfect butting, add two empties to each module: 'SnapStart' and 'SnapEnd' along local +Z.\n" +
            "• If markers are missing, spacing falls back to prefab bounds (Z size).\n" +
            "• Use Modules list for multiple piece types (weighted random).",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    bool CanGenerate()
    {
        if (spline == null) return false;
        if (modules.Count == 0 && fallbackPrefab == null) return false;
        return true;
    }

    // -------- Feedback helpers --------
    void SetInfo(string msg) { _info = msg; _warning = null; _error = null; Repaint(); }
    void SetWarning(string msg) { _warning = msg; _info = null; _error = null; Repaint(); }
    void SetError(string msg) { _error = msg; _info = null; _warning = null; Repaint(); }

    // -------- Generate --------
    void Generate()
    {
        if (spline == null) { SetError("Assign a Spline."); return; }
        var s = spline.Spline;
        if (s == null || s.Count < 2) { SetError("Spline has too few knots."); return; }

        // Ensure parent exists
        if (parent == null)
        {
            var holder = new GameObject("TrackPieces_" + (spline ? spline.name : "Spline"));
            parent = holder.transform;
        }

        // Auto quality if requested
        if (autoQuality)
        {
            lengthSteps = AutoStepsForLength(s);
            mapSteps = Mathf.Max(mapSteps, lengthSteps);
        }

        EditorUtil.WithUndo(parent, "Generate Track Pieces", () =>
        {
            float totalLen = ApproximateLength(s, lengthSteps);
            float usable = Mathf.Max(0f, totalLen - startOffset - endOffset);
            if (usable <= 0.01f) { SetWarning("Usable length is too small with current offsets."); return; }

            var world = spline.transform.localToWorldMatrix;

            int placed = 0;
            float d = 0f;
            while (d <= usable + 1e-4f)
            {
                float t = DistanceToT(s, startOffset + d, mapSteps);

                // Sample frame
                var posLocal = (Vector3)s.EvaluatePosition(t);
                var tanLocal = ((Vector3)s.EvaluateTangent(t)).normalized;
                var upLocal = ((Vector3)s.EvaluateUpVector(t)).normalized;
                if (upLocal.sqrMagnitude < 1e-6f) upLocal = Vector3.up;

                Vector3 pos = world.MultiplyPoint3x4(posLocal);
                Vector3 tan = world.MultiplyVector(tanLocal).normalized;
                Vector3 up = world.MultiplyVector(upLocal).normalized;

                // Rotation with optional yaw/roll tweaks
                var rot = Quaternion.LookRotation(tan, up);
                rot = rot * Quaternion.AngleAxis(yawOffsetDeg, Vector3.up)
                         * Quaternion.AngleAxis(rollOffsetDeg, Vector3.forward);

                // Pick module & compute its step length
                var pick = PickModulePrefab();
                if (pick == null)
                {
                    SetError("No prefab available. Add a module or set Fallback Prefab.");
                    return;
                }

                float moduleLen, startToPivot;
                float stepAdvance;

                if (autoSpacingFromPrefab && TryGetSnapLength(pick, out moduleLen, out startToPivot))
                    stepAdvance = moduleLen;
                else if (autoSpacingFromPrefab)
                    stepAdvance = EstimatePrefabLengthZ(pick);
                else
                    stepAdvance = spacing;

                // Instantiate
                var go = EditorUtil.InstantiatePrefab(pick, parent);
                go.name = $"Piece_{parent.childCount:000}";

                // Marker-aware placement: put SnapStart exactly at spline sample point
                var startMarker = go.transform.Find("SnapStart");
                if (startMarker)
                {
                    var markerLocal = startMarker.localPosition + localPositionOffset;
                    var worldDelta = rot * markerLocal;
                    go.transform.SetPositionAndRotation(pos - worldDelta, rot);
                }
                else
                {
                    // Pivot fallback; if we know the offset to start, apply it; else just use localPositionOffset
                    float push = 0f;
                    if (TryGetSnapLength(pick, out moduleLen, out startToPivot)) push = startToPivot;
                    var worldDelta = rot * (new Vector3(0, 0, push) + localPositionOffset);
                    go.transform.SetPositionAndRotation(pos - worldDelta, rot);
                }

                if (!alignToTangent)
                {
                    go.transform.position = pos;
                }

                placed++;
                d += Mathf.Max(0.001f, stepAdvance);
            }

            SetInfo($"Generated {placed} piece(s) under '{parent.name}'.");
        });
    }

    // -------- Clear --------
    void ClearParentChildren()
    {
        if (parent == null) { SetWarning("No Parent assigned."); return; }
        EditorUtil.WithUndo(parent, "Clear Children", () =>
        {
            var toDelete = new List<GameObject>();
            foreach (Transform c in parent) toDelete.Add(c.gameObject);

            foreach (var go in toDelete)
            {
                if (!Application.isPlaying) Object.DestroyImmediate(go);
                else Object.Destroy(go);
            }
        });
        SetInfo("Cleared all children of Parent.");
    }

    // -------- Helpers: spacing/markers --------
    static bool TryGetSnapLength(GameObject prefab, out float length, out float startToPivot)
    {
        length = 0f; startToPivot = 0f;
        if (prefab == null) return false;

        // Note: Find uses runtime instance hierarchy; when measuring from the asset, we rely on default transform layout.
        var preview = prefab.transform;
        var start = preview.Find("SnapStart");
        var end = preview.Find("SnapEnd");
        if (!start || !end) return false;

        var localStart = start.localPosition;
        var localEnd = end.localPosition;

        length = Mathf.Max(0.001f, (localEnd - localStart).magnitude);
        // distance along +Z from prefab pivot to start marker (negative if start is forward of pivot)
        startToPivot = -localStart.z;
        return true;
    }

    static float EstimatePrefabLengthZ(GameObject prefab)
    {
        if (prefab == null) return 1f;
        var rends = prefab.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return 1f;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        // Approximate Z length by projecting size to prefab's local Z.
        // Since bounds are world-aligned, use a conservative average of X/Y/Z.
        // If your modules are axis-aligned with +Z forward, consider b.size.z directly.
        return Mathf.Max(0.001f, b.size.z);
    }

    GameObject PickModulePrefab()
    {
        if (modules == null || modules.Count == 0)
            return fallbackPrefab;

        float sum = 0f;
        for (int i = 0; i < modules.Count; i++)
            sum += Mathf.Max(0f, modules[i].weight);
        if (sum <= 0f)
            return modules[0].prefab != null ? modules[0].prefab : fallbackPrefab;

        float r = Random.value * sum;
        for (int i = 0; i < modules.Count; i++)
        {
            float w = Mathf.Max(0f, modules[i].weight);
            if (r <= w) return modules[i].prefab != null ? modules[i].prefab : fallbackPrefab;
            r -= w;
        }
        return modules[modules.Count - 1].prefab != null ? modules[modules.Count - 1].prefab : fallbackPrefab;
    }

    // -------- Helpers: sampling --------
    static float ApproximateLength(Spline s, int steps)
    {
        Vector3 prev = s.EvaluatePosition(0f);
        float len = 0f;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 p = s.EvaluatePosition(t);
            len += Vector3.Distance(prev, p);
            prev = p;
        }
        return len;
    }

    static float DistanceToT(Spline s, float distance, int steps)
    {
        distance = Mathf.Max(0f, distance);
        Vector3 prev = s.EvaluatePosition(0f);
        float accum = 0f;

        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 p = s.EvaluatePosition(t);
            float seg = Vector3.Distance(prev, p);

            if (accum + seg >= distance)
            {
                float remain = distance - accum;
                float segT = seg > 1e-5f ? (remain / seg) : 0f;
                float tPrev = (i - 1) / (float)steps;
                return Mathf.Lerp(tPrev, t, segT);
            }

            accum += seg;
            prev = p;
        }
        return 1f;
    }

    static int AutoStepsForLength(Spline s, int min = 256, int max = 8192)
    {
        float rough = ApproximateLength(s, 128);
        int steps = Mathf.CeilToInt(rough / 0.25f); // ~25cm per step
        return Mathf.Clamp(steps, min, max);
    }
}

// -------- Editor utilities (Undo + prefab-safe instantiate) --------
static class EditorUtil
{
    public static void WithUndo(Object targetRoot, string label, System.Action act)
    {
        Undo.RegisterFullObjectHierarchyUndo(targetRoot, label);
        try { act?.Invoke(); }
        finally
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }

    public static GameObject InstantiatePrefab(GameObject prefab, Transform parent = null)
    {
        GameObject go = null;
        if (prefab != null)
        {
            go = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (go == null) go = Object.Instantiate(prefab, parent);
        }
        return go;
    }
}
