using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

public class PathShadowSegmentChecker : MonoBehaviour
{
    public Transform startPoint;
    public Transform endPoint;
    public LayerMask roadLayer;          // 道路所在的 Layer，例如 "Road"
    public int pathSampleCount = 200;    // 路径采样密度（越高越准，建议 100~500）

    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();
        //lineRenderer.material = new Material(Shader.Find("Unlit/Color"));
        lineRenderer.startColor = Color.red;
        lineRenderer.endColor = Color.red;
        lineRenderer.widthMultiplier = 0.2f;

        EncodePathShadowPattern();
    }

   public void EncodePathShadowPattern()
    {
        // 1. 计算 A→B 的 NavMesh 路径
        NavMeshPath navPath = new NavMeshPath();
        if (!NavMesh.CalculatePath(startPoint.position, endPoint.position, NavMesh.AllAreas, navPath) || navPath.corners.Length < 2)
        {
            Debug.LogError("无法计算路径！请检查：\n- 是否烘焙 NavMesh\n- 起点/终点是否在可行走区域");
            return;
        }

        List<Vector3> pathCorners = new List<Vector3>(navPath.corners);
        DrawPath(pathCorners);

        // 2. 数据结构：记录每段路的 (总采样点数, 阴影点数)
        var segmentStats = new Dictionary<RoadSegment, (int total, int shadow)>();
        var orderedSegments = new List<RoadSegment>(); // 按路径顺序（去重）
        var seenSegments = new HashSet<RoadSegment>();

        // 3. 在路径上密集采样
        for (int i = 0; i < pathSampleCount; i++)
        {
            float t = (float)i / (pathSampleCount - 1);
            Vector3 posOnPath = GetPointOnPath(pathCorners, t);
            Vector3 samplePos = posOnPath + Vector3.up * 0.4f; // 抬高避免地面碰撞

            // 检测该点属于哪个 RoadSegment
            Collider[] hits = Physics.OverlapSphere(samplePos, 0.6f, roadLayer);
            RoadSegment currentSegment = null;

            foreach (var hit in hits)
            {
                currentSegment = hit.GetComponent<RoadSegment>();
                if (currentSegment != null) break;
            }

            if (currentSegment == null) continue; // 不在任何路段上（跳过）

            // 更新统计
            if (!segmentStats.ContainsKey(currentSegment))
                segmentStats[currentSegment] = (0, 0);

            var (total, shadow) = segmentStats[currentSegment];
            bool inShadow = IsInShadow(samplePos);
            segmentStats[currentSegment] = (total + 1, shadow + (inShadow ? 1 : 0));

            // 记录顺序（首次出现）
            if (!seenSegments.Contains(currentSegment))
            {
                seenSegments.Add(currentSegment);
                orderedSegments.Add(currentSegment);
            }
        }

        // 4. 输出结果：每段生成 10 位编码
        if (orderedSegments.Count == 0)
        {
            Debug.LogWarning("路径未经过任何 RoadSegment。");
            return;
        }

        foreach (var seg in orderedSegments)
        {
            var (total, shadow) = segmentStats[seg];
            float ratio = (float)shadow / total; // 阴影比例

            // 转换为 10 位：前 N 位为 '1'，其余为 '0'
            int onesCount = Mathf.RoundToInt(ratio * 10);
            string binaryCode = new string('1', onesCount) + new string('0', 10 - onesCount);

            Debug.Log($"{seg.name}：{binaryCode}");
        }
    }

    // 沿路径插值得到任意 t ∈ [0,1] 的点
    Vector3 GetPointOnPath(List<Vector3> path, float t)
    {
        if (t <= 0) return path[0];
        if (t >= 1) return path[path.Count - 1];

        float totalLength = 0f;
        var lengths = new List<float>();
        for (int i = 0; i < path.Count - 1; i++)
        {
            float d = Vector3.Distance(path[i], path[i + 1]);
            lengths.Add(d);
            totalLength += d;
        }

        float targetDist = t * totalLength;
        float walked = 0f;

        for (int i = 0; i < lengths.Count; i++)
        {
            if (walked + lengths[i] >= targetDist)
            {
                float segmentT = (targetDist - walked) / lengths[i];
                return Vector3.Lerp(path[i], path[i + 1], segmentT);
            }
            walked += lengths[i];
        }

        return path[path.Count - 1];
    }

    // 阴影检测：Raycast 到太阳方向，排除道路/地面自身
    bool IsInShadow(Vector3 position)
    {
        Light sun = FindObjectOfType<Light>();
        if (sun == null || sun.type != LightType.Directional)
            return false;

        Vector3 lightDir = -sun.transform.forward;
        float maxDistance = 100f;

        // 使用 QueryTriggerInteraction.Collide 确保触发器不影响（如果你用了 Trigger）
        if (Physics.Raycast(position, lightDir, out RaycastHit hit, maxDistance))
        {
            // 关键：如果击中的物体是 Road 或 Ground 层，视为“无遮挡”
            int roadGroundMask = LayerMask.GetMask("Road", "Ground");
            if ((roadGroundMask & (1 << hit.collider.gameObject.layer)) != 0)
            {
                return false; // 是地面/道路 → 阳光直射
            }

            // 否则，击中了建筑、树等 → 阴影
            return true;
        }

        return false; // 无遮挡 → 阳光
    }

    // 可视化路径
    void DrawPath(List<Vector3> path)
    {
        lineRenderer.positionCount = path.Count;
        lineRenderer.SetPositions(path.ToArray());
    }
}
