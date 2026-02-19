using UnityEngine;

//[ExecuteAlways]
public class SunMotion : MonoBehaviour
{
    public bool isPlay = true;
    public float dayLengthSeconds = 60f;      // 一整天持续多少秒（游戏时间）
    public float worldTiltY = 0f;             // 世界 Y 轴倾斜（如地球自转轴倾角）

    private float startTime;                  // 记录开始时间（真实时间）
    private float elapsedTime;                // 累计的游戏时间（受 isPlay 控制）

    void Start()
    {
        startTime = Time.time; // 可选：也可设为 0，取决于是否要与真实时间对齐
        elapsedTime = 0f;
    }

    void Update()
    {
        if (!isPlay) return;

        // 使用 deltaTime 累加经过的游戏时间
        elapsedTime += Time.deltaTime;

        // 归一化到 [0, 1)，表示一天中的进度
        float t = (elapsedTime % dayLengthSeconds) / dayLengthSeconds;

        // 映射：0 → -90°（日出前），0.25 → 0°（正东？），0.5 → 90°（天顶），0.75 → 180°（正西），1 → 270°（午夜）
        float sunAngle = Mathf.Lerp(-90f, 270f, t);

        // 应用旋转：绕 X 轴旋转 sunAngle，再绕 Y 轴旋转 worldTiltY
        transform.rotation = Quaternion.Euler(sunAngle, worldTiltY, 0f);
    }
}
