using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
/// <summary>
/// 基于 NavMesh 的阴影路径规划器。
/// 目标：在起点到终点之间，寻找一条尽可能处于阴影中的路径，
/// 并对路径穿过的 RoadSegment 进行分段阴影编码（如 "10101..."）。
/// </summary>
public class ShadowPathFinderNavMesh : MonoBehaviour
{
    // === 基础路径参数 ===
    public Transform startPoint;          // 起点
    public Transform endPoint;            // 终点
    public int numDetourPoints = 10;      // 每轮生成的绕行点数量
    public float detourRadius = 100f;     // 绕行点采样半径
    public int samplesPerPath = 50;       // 路径阴影评估时的采样点数
    public int optimizationIterations = 5; // 优化迭代次数

    // === 阴影检测参数 ===
    [Tooltip("用于 Raycast 检测遮挡的 LayerMask（应包含建筑、树木等遮挡物）")]
    public LayerMask shadowRaycastMask = -1; // 默认所有层，建议仅包含遮挡物层

    public float raycastOffset = 0.1f;           // 射线起点抬高量，防止自碰撞
    [Header("阴影---->距离")][Range(0f, 1f)] public float shadowTolerance = 0.05f; // 阴影分数容忍度（用于筛选近似最优路径）

    // === 路段阴影分析 ===
    [Header("路段阴影分析")]
    public LayerMask roadLayer = 1 << 0;         // 用于识别道路的 Layer（默认 Default，建议设为 "Road"）
    public int outputCodeLength = 10;            // 每个 RoadSegment 输出的二进制码长度（如 10 位）

    private LineRenderer lineRenderer;
    private List<Vector3> globalBestPath = null;        // 全局最优路径
    private float globalBestShadowScore = -1f;          // 全局最优阴影覆盖率（0~1）

    void Start()
    {
        // 初始化 LineRenderer 用于绘制最终路径
        lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer == null)
            lineRenderer = gameObject.AddComponent<LineRenderer>();

        //lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        lineRenderer.widthMultiplier = 0.4f;
        lineRenderer.positionCount = 0;

        // 开始路径优化
       //RunOptimization();
    }

    /// <summary>
    /// 执行多轮路径优化，尝试找到阴影覆盖率最高的路径。
    /// </summary>
    public void RunOptimization()
    {
        globalBestPath = null;
        globalBestShadowScore = -1f;

        for (int iter = 0; iter < optimizationIterations; iter++)
        {
            FindBestPathThisRound();
        }

        // 绘制并分析最终路径
        if (globalBestPath != null)
        {
            DrawPath(globalBestPath);
            AnalyzeShadowOnRoadSegments(globalBestPath);
        }
        else
        {
            Debug.LogWarning("未找到有效路径。");
        }
    }

    /// <summary>
    /// 对路径经过的每个 RoadSegment 进行分段阴影编码。
    /// 将每个 RoadSegment 沿路径方向均分为 outputCodeLength 段，
    /// 每段根据是否处于阴影输出 '1' 或 '0'。
    /// </summary>
    void AnalyzeShadowOnRoadSegments(List<Vector3> path)
    {
        if (path.Count < 2) return;

        const int denseSamples = 500;             // 用于扫描路径上 RoadSegment 的密集采样点数
        const int samplesPerSubSegment = 5;       // 每小段内部的阴影采样次数（用于投票）

        var segmentTBounds = new Dictionary<RoadSegment, (float minT, float maxT)>();
        var orderedSegments = new List<RoadSegment>();

        // 第一步：扫描整条路径，确定每个 RoadSegment 在路径参数 t ∈ [0,1] 上的覆盖区间
        for (int i = 0; i < denseSamples; i++)
        {
            float t = (float)i / (denseSamples - 1);
            Vector3 posOnPath = GetPointOnPath(path, t);
            Vector3 samplePos = posOnPath + Vector3.up * 0.4f; // 抬高避免地面碰撞

            // 检查当前位置是否在某个 RoadSegment 上
            Collider[] hits = Physics.OverlapSphere(samplePos, 0.6f, roadLayer);
            RoadSegment currentSegment = null;
            foreach (var hit in hits)
            {
                currentSegment = hit.GetComponent<RoadSegment>();
                if (currentSegment != null) break;
            }

            if (currentSegment == null) continue;

            // 记录该 RoadSegment 的 t 范围
            if (!segmentTBounds.ContainsKey(currentSegment))
            {
                segmentTBounds[currentSegment] = (t, t);
                orderedSegments.Add(currentSegment); // 保持顺序
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

        // 第二步：对每个 RoadSegment 进行分段阴影编码
        foreach (var seg in orderedSegments)
        {
            var (tStart, tEnd) = segmentTBounds[seg];
            if (tStart >= tEnd) tEnd = tStart + 0.001f; // 避免除零

            var codeBuilder = new System.Text.StringBuilder(outputCodeLength);

            // 将该 RoadSegment 的 t 区间 [tStart, tEnd] 均分为 outputCodeLength 段
            for (int idx = 0; idx < outputCodeLength; idx++)
            {
                float localT0 = (float)idx / outputCodeLength;
                float localT1 = (float)(idx + 1) / outputCodeLength;
                float globalT0 = Mathf.Lerp(tStart, tEnd, localT0);
                float globalT1 = Mathf.Lerp(tStart, tEnd, localT1);

                int shadowCount = 0;
                // 对每小段进行多次采样，多数投票决定是否为阴影
                for (int s = 0; s < samplesPerSubSegment; s++)
                {
                    float ratio = samplesPerSubSegment > 1 ? (float)s / (samplesPerSubSegment - 1) : 0.5f;
                    float t = Mathf.Lerp(globalT0, globalT1, ratio);
                    Vector3 pt = GetPointOnPath(path, t) + Vector3.up * 0.4f;

                    bool inShadow = IsInShadowAccurate(pt);
                    if (inShadow) shadowCount++;
                    // 注意：已移除 Debug.DrawRay，不再可视化射线
                }

                // 多数投票：超过一半采样点在阴影中，则该段标记为 '1'
                bool segmentInShadow = shadowCount > samplesPerSubSegment / 2;
                codeBuilder.Append(segmentInShadow ? '1' : '0');
            }

            Debug.Log($"{seg.name} 阴影编码：{codeBuilder}");
        }
    }

    /// <summary>
    /// 使用太阳方向进行精确阴影检测（考虑 shadowRaycastMask）。
    /// </summary>
    bool IsInShadowAccurate(Vector3 position)
    {
        Light sun = FindObjectOfType<Light>();
        if (sun == null || sun.type != LightType.Directional)
            return false;

        Vector3 lightDir = -sun.transform.forward; // 太阳光方向（从物体指向太阳的反方向）
        return Physics.Raycast(position, lightDir, 100f, shadowRaycastMask);
    }

    /// <summary>
    /// 单轮路径搜索：生成若干候选路径（直连 + 绕行），评估后更新全局最优。
    /// </summary>
    void FindBestPathThisRound()
    {
        var candidatePaths = new List<List<Vector3>>();

        // 1. 直连路径
        var direct = CalculateNavMeshPath(startPoint.position, endPoint.position);
        if (direct != null && direct.Count >= 2)
            candidatePaths.Add(direct);

        // 2. 随机绕行路径
        for (int i = 0; i < numDetourPoints; i++)
        {
            Vector2 r = Random.insideUnitCircle;
            Vector3 detour = new Vector3(
                startPoint.position.x + r.x * detourRadius,
                startPoint.position.y,
                startPoint.position.z + r.y * detourRadius
            );

            // 确保绕行点在 NavMesh 上
            if (!NavMesh.SamplePosition(detour, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                continue;
            detour = hit.position;

            // 分段计算路径
            var p1 = CalculateNavMeshPath(startPoint.position, detour);
            var p2 = CalculateNavMeshPath(detour, endPoint.position);
            if (p1 == null || p2 == null) continue;

            // 合并路径（去除重复端点）
            var combined = new List<Vector3>(p1);
            combined.RemoveAt(combined.Count - 1);
            combined.AddRange(p2);
            candidatePaths.Add(combined);
        }

        if (candidatePaths.Count == 0) return;

        // 3. 评估所有候选路径
        var evaluations = new List<(List<Vector3> path, float shadowScore, float length)>();
        foreach (var path in candidatePaths)
        {
            float shadowScore = EvaluateShadowOnPath(path);
            float length = CalculatePathLength(path);
            evaluations.Add((new List<Vector3>(path), shadowScore, length));
        }

        if (evaluations.Count == 0) return;

        // 4. 筛选阴影覆盖率接近最大值的路径（容忍 shadowTolerance）
        float maxShadow = evaluations.Max(e => e.shadowScore);
        var viable = evaluations.Where(e => e.shadowScore >= maxShadow - shadowTolerance).ToList();
        if (viable.Count == 0) return;

        // 5. 在可行路径中选择最短的
        var best = viable.OrderBy(e => e.length).First();

        // 6. 更新全局最优（优先阴影覆盖率，其次路径长度）
        bool shouldUpdate = false;
        if (best.shadowScore > globalBestShadowScore)
            shouldUpdate = true;
        else if (Mathf.Abs(best.shadowScore - globalBestShadowScore) <= 0.001f)
        {
            float currentLen = globalBestPath != null ? CalculatePathLength(globalBestPath) : float.MaxValue;
            if (best.length < currentLen) shouldUpdate = true;
        }

        if (shouldUpdate)
        {
            globalBestShadowScore = best.shadowScore;
            globalBestPath = new List<Vector3>(best.path);
        }
    }

    // --- 工具方法 ---

    /// <summary>
    /// 使用 NavMesh 计算两点间路径。
    /// </summary>
    List<Vector3> CalculateNavMeshPath(Vector3 start, Vector3 end)
    {
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) && path.corners.Length >= 2)
            return new List<Vector3>(path.corners);
        return null;
    }

    /// <summary>
    /// 快速阴影检测（用于路径评分）。
    /// </summary>
    bool IsInShadow(Vector3 position)
    {
        Light sun = FindObjectOfType<Light>();
        if (sun == null || sun.type != LightType.Directional) return false;
        return Physics.Raycast(position + Vector3.up * raycastOffset, -sun.transform.forward, 1000f, shadowRaycastMask);
    }

    /// <summary>
    /// 评估路径的阴影覆盖率（0~1）。
    /// </summary>
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

    /// <summary>
    /// 根据归一化参数 t ∈ [0,1] 获取路径上的插值点。
    /// </summary>
    Vector3 GetPointOnPath(List<Vector3> path, float t)
    {
        if (t <= 0) return path[0];
        if (t >= 1) return path[path.Count - 1];

        // 计算总长度和各段长度
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

    /// <summary>
    /// 计算路径总长度。
    /// </summary>
    float CalculatePathLength(List<Vector3> path)
    {
        if (path == null || path.Count < 2) return 0f;
        float total = 0f;
        for (int i = 0; i < path.Count - 1; i++)
            total += Vector3.Distance(path[i], path[i + 1]);
        return total;
    }

    /// <summary>
    /// 使用 LineRenderer 绘制路径。
    /// </summary>
    void DrawPath(List<Vector3> path)
    {
        lineRenderer.positionCount = path.Count;
        lineRenderer.SetPositions(path.ToArray());
    }
}
