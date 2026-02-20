// using UnityEngine;

// //[ExecuteAlways]
// public class SunMotion : MonoBehaviour
// {
//     public bool isPlay = true;
//     public float dayLengthSeconds = 60f;      // 一整天持续多少秒（游戏时间）
//     public float worldTiltY = 0f;             // 世界 Y 轴倾斜（如地球自转轴倾角）

//     private float startTime;                  // 记录开始时间（真实时间）
//     private float elapsedTime;                // 累计的游戏时间（受 isPlay 控制）

//     void Start()
//     {
//         startTime = Time.time; // 可选：也可设为 0，取决于是否要与真实时间对齐
//         elapsedTime = 0f;
//     }

//     void Update()
//     {
//         if (!isPlay) return;

//         // 使用 deltaTime 累加经过的游戏时间
//         elapsedTime += Time.deltaTime;

//         // 归一化到 [0, 1)，表示一天中的进度
//         float t = (elapsedTime % dayLengthSeconds) / dayLengthSeconds;

//         // 映射：0 → -90°（日出前），0.25 → 0°（正东？），0.5 → 90°（天顶），0.75 → 180°（正西），1 → 270°（午夜）
//         float sunAngle = Mathf.Lerp(-90f, 270f, t);

//         // 应用旋转：绕 X 轴旋转 sunAngle，再绕 Y 轴旋转 worldTiltY
//         transform.rotation = Quaternion.Euler(sunAngle, worldTiltY, 0f);
//     }
// }




using UnityEngine;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using TMPro;

[Serializable]
public class SolarPosition
{
    public float zenith;
    public float azimuth;
}

public class SunMotion : MonoBehaviour
{
    [Header("Simulation Settings")]
    public bool isPlay = true;
    public float dayLengthSeconds = 60f;
    public float worldTiltY = 0f;

    [Header("Location Settings")]
    public float latitude = 40.7128f;
    public float longitude = -74.0060f;
    public float altitude = 10f;

    [Header("Simulation Start Date")]
    public int startYear = 2026;
    public int startMonth = 2;
    public int startDay = 7;
    public string timeZone = "America/New_York";

    [Header("UI")]
    public TMP_Text timeDisplay;

    private float elapsedTime = 0f;
    private List<(float zenith, float azimuth)> minuteSolarPositions = new List<(float zenith, float azimuth)>();
    private static readonly HttpClient client = new HttpClient();
    private bool positionsReady = false;
    private bool isRecomputing = false;
    private DateTime currentSimDate;

    async void Start()
    {
        currentSimDate = new DateTime(startYear, startMonth, startDay);
        await WaitForServer();
        await PrecomputeSolarPositions(currentSimDate);
        elapsedTime = 0f;
        positionsReady = true;
        UnityEngine.Debug.Log("All solar positions ready. Sun movement will begin.");
    }

    async Task WaitForServer()
    {
        UnityEngine.Debug.Log("Waiting for Flask server...");
        while (true)
        {
            try
            {
                await client.GetStringAsync("http://localhost:5000/health");
                UnityEngine.Debug.Log("Flask server ready!");
                return;
            }
            catch
            {
                UnityEngine.Debug.Log("Server not ready, please start Flask in terminal. Retrying...");
                await Task.Delay(2000);
            }
        }
    }

    void Update()
    {
        if (!isPlay || !positionsReady) return;

        elapsedTime += Time.deltaTime;

        // detect day rollover
        if (elapsedTime >= dayLengthSeconds && !isRecomputing)
        {
            elapsedTime = 0f;
            currentSimDate = currentSimDate.AddDays(1);
            positionsReady = false;
            isRecomputing = true;
            _ = RecomputeNextDay();
        }

        float t = (elapsedTime % dayLengthSeconds) / dayLengthSeconds;
        float simulatedMinutes = t * 24f * 60f;

        int minuteIndex = Mathf.FloorToInt(simulatedMinutes) % 1440;
        int nextIndex = (minuteIndex + 1) % 1440;
        float lerpT = simulatedMinutes - minuteIndex;

        var curr = minuteSolarPositions[minuteIndex];
        var next = minuteSolarPositions[nextIndex];

        float zenith = Mathf.Lerp(curr.zenith, next.zenith, lerpT);
        float azimuth = Mathf.LerpAngle(curr.azimuth, next.azimuth, lerpT);

        // float sunAngle = 90f - zenith;

        // if (zenith < 90f)
        // {
        //     transform.rotation = Quaternion.Euler(sunAngle, azimuth + worldTiltY, 0f);
        // }
        // else
        // {
        //     transform.rotation = Quaternion.Euler(-90f, azimuth + worldTiltY, 0f);
        // }
        float sunAngle = 90f - zenith;

        // smooth transition zone around horizon (+/- 5 degrees)
        float blendRange = 5f;
        float blendFactor = Mathf.InverseLerp(90f + blendRange, 90f - blendRange, zenith);

        float parkedAngle = -90f;
        float finalAngle = Mathf.Lerp(parkedAngle, sunAngle, blendFactor);

        transform.rotation = Quaternion.Euler(finalAngle, azimuth + worldTiltY, 0f);

        if (zenith >= 90f)
        {
            // set minimum ambient light at night
            RenderSettings.ambientIntensity = 0.1f;
        }
        else
        {
            // full ambient during day
            RenderSettings.ambientIntensity = 1f;
        }

        int hours = Mathf.FloorToInt(simulatedMinutes / 60f);
        int minutes = Mathf.FloorToInt(simulatedMinutes % 60f);
        if (timeDisplay != null)
            timeDisplay.text = $"{currentSimDate:yyyy-MM-dd} {hours:00}:{minutes:00} | Z: {zenith:F1} A: {azimuth:F1}";
    }

    async Task RecomputeNextDay()
    {
        UnityEngine.Debug.Log($"Computing positions for {currentSimDate:yyyy-MM-dd}");
        await PrecomputeSolarPositions(currentSimDate);
        elapsedTime = 0f;
        isRecomputing = false;
        positionsReady = true;
        UnityEngine.Debug.Log($"New day ready: {currentSimDate:yyyy-MM-dd}");
    }

    async Task PrecomputeSolarPositions(DateTime date)
    {
        minuteSolarPositions.Clear();

        for (int m = 0; m < 24 * 60; m++)
        {
            DateTime dt = date.AddMinutes(m);
            var pos = await GetSunPositionFromAPI(dt, latitude, longitude, altitude);
            minuteSolarPositions.Add(pos);
        }
        minuteSolarPositions = newPositions;
        UnityEngine.Debug.Log($"Finished precomputing positions for {date:yyyy-MM-dd}");
    }

    async Task<(float zenith, float azimuth)> GetSunPositionFromAPI(DateTime time, float lat, float lon, float alt)
    {
        try
        {
            string url = $"http://localhost:5000/solar?time={time:yyyy-MM-dd HH:mm}&lat={lat}&lon={lon}&alt={alt}&tz={timeZone}";
            string response = await client.GetStringAsync(url);
            SolarPosition pos = JsonUtility.FromJson<SolarPosition>(response);
            return (pos.zenith, pos.azimuth);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("API error: " + e.Message);
            float minuteFraction = (time.Hour * 60 + time.Minute) / 1440f;
            float zenith = Mathf.Lerp(90f, 0f, Mathf.Sin(minuteFraction * Mathf.PI));
            float azimuth = minuteFraction * 360f;
            return (zenith, azimuth);
        }
    }
}