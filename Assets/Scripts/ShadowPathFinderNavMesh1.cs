using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;


public class ShadowPathFinderNavMesh1 : MonoBehaviour
{
 
    public Transform startPoint;
    public Transform endPoint;
    public int numDetourPoints = 10;
    public float detourRadius = 100f;
    public int samplesPerPath = 50;
    public int optimizationIterations = 5;

    [Tooltip("用于 Raycast 检测遮挡的 LayerMask（包含建筑、树木等遮挡物）")]
    public LayerMask shadowRaycastMask = -1;
    public float raycastOffset = 0.1f;

    [Header("路段阴影分析")]
    public LayerMask roadLayer = 1 << 0;
    public int outputCodeLength = 10;

    [Header("路径可视化限制")]
    public int maxPathsToVisualize = 30;
    public int numLongestToRemove = 5;
    private List<GameObject> pathRenderers = new List<GameObject>();
    public bool visualizeAllPaths = true;
    public bool highlightBestPath = true;

    // === 用于存储最终用于显示的路径信息 ===
    private List<(List<Vector3> path, float shadowScore, float length)> displayedPaths = new();
    private List<Color> displayedPathColors = new(); // 与 displayedPaths 对应的颜色
    private List<Vector3> globalBestPath = null;
    private float globalBestShadowScore = -1f;

    void Start()
    {
    }

    public void RunOptimization()
    {
        foreach (var go in pathRenderers)
            if (go != null) Destroy(go);
        pathRenderers.Clear();

        var allValidPaths = new List<(List<Vector3> path, float shadowScore, float length)>();

        for (int iter = 0; iter < optimizationIterations; iter++)
        {
            allValidPaths.AddRange(FindAllCandidatePaths());
        }

        if (allValidPaths.Count == 0)
        {
            Debug.LogWarning("未找到任何有效路径。");
            displayedPaths.Clear();
            displayedPathColors.Clear(); // 清空颜色
            return;
        }

        var sortedByQuality = allValidPaths
            .OrderByDescending(p => p.shadowScore)
            .ThenBy(p => p.length)
            .ToList();

        var best = sortedByQuality.First();
        globalBestPath = new List<Vector3>(best.path);
        globalBestShadowScore = best.shadowScore;

        var uniquePaths = sortedByQuality
            .GroupBy(p => string.Join(",", p.path.Select(v => $"{v.x:F2},{v.z:F2}")))
            .Select(g => g.First())
            .ToList();

        var pathsWithoutLongest = uniquePaths
            .Where(p => p.path.SequenceEqual(best.path))
            .Concat(
                uniquePaths
                    .Where(p => !p.path.SequenceEqual(best.path))
                    .OrderBy(p => p.length)
                    .Take(Math.Max(0, uniquePaths.Count - 1 - numLongestToRemove))
            )
            .ToList();

        displayedPaths = pathsWithoutLongest.Take(maxPathsToVisualize).ToList();

        //  生成displayedPaths对应的颜色
        displayedPathColors.Clear();
        for (int i = 0; i < displayedPaths.Count; i++)
        {
            var (path, _, _) = displayedPaths[i];
            Color color = path.SequenceEqual(globalBestPath)
                ? Color.red
                : Color.HSVToRGB((float)i / displayedPaths.Count, 0.8f, 0.9f);
            displayedPathColors.Add(color);

            if (visualizeAllPaths)
            {
                DrawPathAsChild(path, color);
            }
        }

        AnalyzeShadowOnRoadSegments(globalBestPath);
    }

    // ========== 其余方法保持不变 ==========

    List<(List<Vector3> path, float shadowScore, float length)> FindAllCandidatePaths()
    {
        var candidatePaths = new List<List<Vector3>>();

        var direct = CalculateNavMeshPath(startPoint.position, endPoint.position);
        if (direct != null && direct.Count >= 2)
            candidatePaths.Add(direct);

        for (int i = 0; i < numDetourPoints; i++)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle;
            Vector3 detour = new Vector3(
                startPoint.position.x + r.x * detourRadius,
                startPoint.position.y,
                startPoint.position.z + r.y * detourRadius
            );

            if (!NavMesh.SamplePosition(detour, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                continue;
            detour = hit.position;

            var p1 = CalculateNavMeshPath(startPoint.position, detour);
            var p2 = CalculateNavMeshPath(detour, endPoint.position);
            if (p1 == null || p2 == null) continue;

            var combined = new List<Vector3>(p1);
            combined.RemoveAt(combined.Count - 1);
            combined.AddRange(p2);
            candidatePaths.Add(combined);
        }

        var evaluations = new List<(List<Vector3>, float, float)>();
        foreach (var path in candidatePaths)
        {
            float shadowScore = EvaluateShadowOnPath(path);
            float length = CalculatePathLength(path);
            evaluations.Add((new List<Vector3>(path), shadowScore, length));
        }

        return evaluations;
    }

    void DrawPathAsChild(List<Vector3> path, Color color)
    {
        GameObject obj = new GameObject("ShadowPath_" + pathRenderers.Count);
        obj.transform.SetParent(transform, false);

        LineRenderer lr = obj.AddComponent<LineRenderer>();
        lr.positionCount = path.Count;
        lr.SetPositions(path.ToArray());
        lr.startColor = color;
        lr.endColor = color;
        lr.widthMultiplier = 1f;
        lr.material = new Material(Shader.Find("Unlit/Color")) { color = color };

        pathRenderers.Add(obj);
    }

    // ========== 工具方法 ==========

    void AnalyzeShadowOnRoadSegments(List<Vector3> path)
    {
        if (path.Count < 2) return;

        const int denseSamples = 500;
        const int samplesPerSubSegment = 5;

        var segmentTBounds = new Dictionary<RoadSegment, (float minT, float maxT)>();
        var orderedSegments = new List<RoadSegment>();

        for (int i = 0; i < denseSamples; i++)
        {
            float t = (float)i / (denseSamples - 1);
            Vector3 posOnPath = GetPointOnPath(path, t);
            Vector3 samplePos = posOnPath + Vector3.up * 0.4f;

            Collider[] hits = Physics.OverlapSphere(samplePos, 0.6f, roadLayer);
            RoadSegment currentSegment = null;
            foreach (var hit in hits)
            {
                currentSegment = hit.GetComponent<RoadSegment>();
                if (currentSegment != null) break;
            }

            if (currentSegment == null) continue;

            if (!segmentTBounds.ContainsKey(currentSegment))
            {
                segmentTBounds[currentSegment] = (t, t);
                orderedSegments.Add(currentSegment);
            }
            else
            {
                var (minT, maxT) = segmentTBounds[currentSegment];
                segmentTBounds[currentSegment] = (Mathf.Min(minT, t), Mathf.Max(maxT, t));
            }
        }

        if (segmentTBounds.Count == 0)
        {
            Debug.LogWarning("路径未经过任何 RoadSegment，请检查 roadLayer 设置。");
            return;
        }

        foreach (var seg in orderedSegments)
        {
            var (tStart, tEnd) = segmentTBounds[seg];
            if (tStart >= tEnd) tEnd = tStart + 0.001f;

            var codeBuilder = new System.Text.StringBuilder(outputCodeLength);

            for (int idx = 0; idx < outputCodeLength; idx++)
            {
                float localT0 = (float)idx / outputCodeLength;
                float localT1 = (float)(idx + 1) / outputCodeLength;
                float globalT0 = Mathf.Lerp(tStart, tEnd, localT0);
                float globalT1 = Mathf.Lerp(tStart, tEnd, localT1);

                int shadowCount = 0;
                for (int s = 0; s < samplesPerSubSegment; s++)
                {
                    float ratio = samplesPerSubSegment > 1 ? (float)s / (samplesPerSubSegment - 1) : 0.5f;
                    float t = Mathf.Lerp(globalT0, globalT1, ratio);
                    Vector3 pt = GetPointOnPath(path, t) + Vector3.up * 0.4f;

                    bool inShadow = IsInShadowAccurate(pt);
                    if (inShadow) shadowCount++;
                }

                bool segmentInShadow = shadowCount > samplesPerSubSegment / 2;
                codeBuilder.Append(segmentInShadow ? '1' : '0');
            }

            Debug.Log($"{seg.name} 阴影编码：{codeBuilder}");
        }
    }

    bool IsInShadowAccurate(Vector3 position)
    {
        Light sun = FindObjectOfType<Light>();
        if (sun == null || sun.type != LightType.Directional)
            return false;

        Vector3 lightDir = -sun.transform.forward;
        return Physics.Raycast(position, lightDir, 100f, shadowRaycastMask);
    }

    List<Vector3> CalculateNavMeshPath(Vector3 start, Vector3 end)
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) && path.corners.Length >= 2)
            return new List<Vector3>(path.corners);
        return null;
    }

    bool IsInShadow(Vector3 position)
    {
        Light sun = FindObjectOfType<Light>();
        if (sun == null || sun.type != LightType.Directional) return false;
        return Physics.Raycast(position + Vector3.up * raycastOffset, -sun.transform.forward, 1000f, shadowRaycastMask);
    }

    float EvaluateShadowOnPath(List<Vector3> path)
    {
        if (path.Count < 2) return 0f;
        float shadowCount = 0f;
        for (int i = 0; i < samplesPerPath; i++)
        {
            float t = (float)i / (samplesPerPath - 1);
            Vector3 pos = GetPointOnPath(path, t);
            if (IsInShadow(pos)) shadowCount++;
        }
        return shadowCount / samplesPerPath;
    }

    Vector3 GetPointOnPath(List<Vector3> path, float t)
    {
        if (t <= 0) return path[0];
        if (t >= 1) return path[path.Count - 1];

        float totalLen = 0f;
        var segLens = new List<float>();
        for (int i = 0; i < path.Count - 1; i++)
        {
            float len = Vector3.Distance(path[i], path[i + 1]);
            segLens.Add(len);
            totalLen += len;
        }

        if (totalLen == 0) return path[0];

        float target = t * totalLen;
        float walked = 0f;
        for (int i = 0; i < segLens.Count; i++)
        {
            if (walked + segLens[i] >= target)
            {
                float segT = (target - walked) / segLens[i];
                return Vector3.Lerp(path[i], path[i + 1], segT);
            }
            walked += segLens[i];
        }
        return path[path.Count - 1];
    }

    float CalculatePathLength(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return 0f;
        float total = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            total += Vector3.Distance(path[i], path[i + 1]);
        return total;
    }

    // ============ GUI 表格显示============
    private void OnGUI()
    {
        if (displayedPaths == null || displayedPaths.Count == 0 || displayedPathColors.Count != displayedPaths.Count)
            return;

        // 表格配置
        float tableWidth = 400f;
        float rowHeight = 35f;
        float[] columnWidths = { 100f, 150f, 170f }; // 颜色 | 长度 | 面积

        float startX = Screen.width - tableWidth - 10f;
        float startY = 21f;

        GUIStyle baseStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 21,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white }
        };
        GUIStyle headerStyle = new GUIStyle(baseStyle)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.black }
        };
        GUIStyle bestRowStyle = new GUIStyle(baseStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.yellow },
            fontSize =21
        };

        // 表头
        GUI.Label(new Rect(startX, startY, columnWidths[0], rowHeight), "Route", headerStyle);
        GUI.Label(new Rect(startX + columnWidths[0], startY, columnWidths[1], rowHeight), "Distance", headerStyle);
        GUI.Label(new Rect(startX + columnWidths[0] + columnWidths[1]-10, startY, columnWidths[2], rowHeight), " Exposure", headerStyle);

        startY += rowHeight;
        float dataSpacing = 32f;
        // 数据行
        for (int i = 0; i < displayedPaths.Count; i++)
        {
            var (path, shadowScore, length) = displayedPaths[i];
            Color pathColor = displayedPathColors[i]; // 预先保存的颜色
            bool isBest = path.SequenceEqual(globalBestPath);

            GUIStyle rowStyle = isBest ? bestRowStyle : baseStyle;

            string lenText = $"{length:F1}";
            string lightText = $"{(1f - shadowScore) * 100:F1}";

            // 绘制颜色块
            // Color oldColor = GUI.color;
            var colorBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Texture2D.whiteTexture },
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };

            Color oldColor = GUI.color;
            GUI.color = pathColor;
            GUI.Box(new Rect(startX, startY, columnWidths[0], rowHeight), GUIContent.none, colorBoxStyle);
            GUI.color = oldColor;

            GUI.Label(new Rect(startX + columnWidths[0]+ dataSpacing, startY, columnWidths[1], rowHeight), lenText, rowStyle);
            GUI.Label(new Rect(startX + columnWidths[0] + columnWidths[1]+32, startY, columnWidths[2], rowHeight), lightText, rowStyle);

            startY += rowHeight;
        }
    }
}