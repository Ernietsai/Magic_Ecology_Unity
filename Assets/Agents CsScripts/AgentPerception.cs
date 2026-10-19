using System.Runtime.CompilerServices;
using UnityEngine;

public class AgentPerception : MonoBehaviour
{
    [Header("感官屬性")]
    public float mentalSense = 1.0f; // 基礎神識屬性 (後天可修煉)
    public float baseRange = 10f;    // 基礎感知範圍

    // 計算當前感官距離上限
    public float MaxPerceptionRange => baseRange * (1f + Mathf.Log(mentalSense + 1f));

    private GridManager gridManager;
    private InternalEnergySystem energySystem;
    private InternalFurnace furnace;
    private AgentMagic magicStats;
    [HideInInspector]public Rigidbody rb;
    private float controlCriteria = 0.6f;
    private int agentLayerMask;
    [HideInInspector]public bool UpdateCamera = true;

    void Awake()
    {
        energySystem = GetComponent<InternalEnergySystem>();
        furnace = GetComponent<InternalFurnace>();
        magicStats = GetComponent<AgentMagic>();
        rb = GetComponent<Rigidbody>();
        // 在類別頂部定義 Mask (或在 Inspector 指定)
        agentLayerMask = 1 << LayerMask.NameToLayer("Agents");
        //Debug.Log("Max Perception Range: " + MaxPerceptionRange);
        gridManager = GridManager.Instance;
    }

    private void Start()
    {

    }

    // 環境感知：五行雷達 (Environment Radar)
    public float[] GetEnvironmentData(int rayCount = 8, int segmentCount = 3)
    {
        // x每條射線：[段數(3) * (5種元素 + 1種地形)] = 18 個數據點
        // 總數據量 = 射線數 * 每條射線數據點 = rayCount * 27
        // 速度數據建議只取全射線平均以節省空間
        float[] envData = new float[rayCount * (segmentCount * 13)];
        float maxRange = MaxPerceptionRange;
        float segmentLength = maxRange / segmentCount;
        // 非線性分段比例：近(15%), 中(35%), 遠(50%)
        float[] segmentScales = { 0.15f, 0.35f, 0.50f };

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * (360f / rayCount);
            Vector3 dir = Quaternion.Euler(0, angle, 0) * transform.forward;
            int rayOffset = i * (segmentCount * 13);
            float currentDist = 0;

            for (int s = 0; s < segmentCount; s++)
            {
                // 在每一段內進行多點採樣（例如取 3 個點求平均）
                float segLen = maxRange * segmentScales[s];
                Vector3 segmentStart = transform.position + dir * currentDist;
                Vector3 segmentEnd = transform.position + dir * (currentDist + segLen);

                int samples = 2 + s;
                var segmentAvg = SampleSegment(segmentStart, segmentEnd, samples);

                int segOffset = rayOffset + (s * 7);
                envData[segOffset + 0] = segmentAvg.metal;
                envData[segOffset + 1] = segmentAvg.wood;
                envData[segOffset + 2] = segmentAvg.water;
                envData[segOffset + 3] = segmentAvg.fire;
                envData[segOffset + 4] = segmentAvg.earth;
                envData[segOffset + 5] = segmentAvg.scent;
                envData[segOffset + 6] = gridManager.GetFrequencyMatch(segmentAvg.frequency, magicStats.currentFrequency); // 頻率
                envData[segOffset + 7] = gridManager.GetFrequencyMatch(segmentAvg.frequency, magicStats.currentFrequency, segmentAvg.controlValue); // 控制值
                envData[segOffset + 8] = (gridManager.GetFrequencyMatch(segmentAvg.frequency, magicStats.currentFrequency, segmentAvg.controlValue) > controlCriteria) ? (1) : (-1); // 是否控制
                envData[segOffset + 9] = segmentAvg.terrainType; // 地形
                // 該方向的整體流動趨勢
                Vector3 midPoint = transform.position + dir * ((s + 0.5f) * segmentLength);
                var midCell = gridManager.GetElementAt(midPoint);
                envData[segOffset + 10] = midCell.velocity.x;
                envData[segOffset + 11] = midCell.velocity.y;
                envData[segOffset + 12] = midCell.velocity.z;

                currentDist += segLen;
            }
            // 在 AgentPerception.cs 負責收集數據的地方 (例如 GetEnvironmentData 結束前)
            //偵錯用，之後刪除
            for (int j = 0; j < envData.Length; j++)
            {
                if (float.IsNaN(envData[j]) || float.IsInfinity(envData[j]))
                {
                    Debug.LogError($"警告！第 {j} 個觀測值變成了 NaN！");
                    envData[j] = 0f; // 強制歸零，防止 ML-Agents 崩潰
                }
            }

        }
        return envData;
    }

    // 段內採樣輔助函式
    private SampleData SampleSegment(Vector3 start, Vector3 end, int samplePoints)
    {
        SampleData total = new SampleData();
        for (int i = 0; i < samplePoints; i++)
        {
            float t = (i + 0.5f) / samplePoints;
            Vector3 pos = Vector3.Lerp(start, end, t);
            var cell = gridManager.GetElementAt(pos);

            total.AddFromCell(cell);
            // total.terrainType += cell.terrainType;
        }

        // 求平均
        total.DividedBy(samplePoints!=0 ? samplePoints : 1);
        return total;
    }

    private struct SampleData
    {
        public float metal, wood, water, fire, earth;
        public float scent;
        public float frequency, controlValue;
        public float terrainType;
        public void AddFromCell(GridManager.Cell cell)
        {
            metal += cell.elements[0];
            wood += cell.elements[1];
            water += cell.elements[2];
            fire += cell.elements[3];
            earth += cell.elements[4];
            frequency += cell.frequency;
            controlValue += cell.controlValue;
            scent += cell.scent;
            // terrainType += cell.terrainType; // 假設 Cell 結構有地形類型
        }
        public void DividedBy(int number)
        {
            metal /= number;
            wood /= number;
            water /= number;
            fire /= number;
            earth /= number;
            scent /= number;
            frequency /= number;
            controlValue /= number;
            // terrainType /= number;
        }
    }
    public float[] GetFocusData(Vector2 focusOutput)
    {
        // focusOutput.x = 角度 (-1 to 1 映射到 0-360)
        // focusOutput.y = 距離 (0 to 1 映射到 MaxPerceptionRange)

        float angle = focusOutput.x * 180f;
        float dist = focusOutput.y * MaxPerceptionRange;
        Vector3 focusPos = transform.position + Quaternion.Euler(0, angle, 0) * transform.forward * dist;

        // 採樣聚焦中心點周圍的 5 個高精度數據：[中心, 前, 後, 左, 右]
        float[] focusData = new float[5 * 13]; // 5點 * (5元素 + 1氣味 + 1地形)

        Vector3[] offsets = { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        for (int i = 0; i < 5; i++)
        {
            Vector3 samplePos = focusPos + offsets[i] * gridManager.cellSize; // 以 1 Cell 為精度的微調
            var cell = gridManager.GetElementAt(samplePos);

            int offset = i * 13;
            focusData[offset + 0] = cell.elements[0]; // 金
            focusData[offset + 1] = cell.elements[1]; // 木
            focusData[offset + 2] = cell.elements[2]; // 水
            focusData[offset + 3] = cell.elements[3]; // 火
            focusData[offset + 4] = cell.elements[4]; // 土
            focusData[offset + 5] = cell.scent;
            focusData[offset + 6] = gridManager.GetFrequencyMatch(cell.frequency, magicStats.currentFrequency);
            focusData[offset + 7] = gridManager.GetFrequencyMatch(cell.frequency, magicStats.currentFrequency, cell.controlValue); // 控制值
            focusData[offset + 8] = (gridManager.GetFrequencyMatch(cell.frequency, magicStats.currentFrequency, cell.controlValue) > controlCriteria) ? (1) : (-1); // 是否控制
            focusData[offset + 9] = 0f; // 地形
            focusData[offset + 10] = cell.velocity.x;
            focusData[offset + 11] = cell.velocity.y;
            focusData[offset + 12] = cell.velocity.z;
        }
        //偵錯用，之後刪除
        for (int i = 0; i < focusData.Length; i++)
        {
            if (float.IsNaN(focusData[i]) || float.IsInfinity(focusData[i]))
            {
                Debug.LogError($"在索引 {i} 處發現 NaN 或 Infinity！");
                focusData[i] = 0f; // 發現 NaN 直接歸零，防止毒死模型
            }
        }
        return focusData;
    }

    // 實體感知：生命體與秘笈 (Entity Detection)
    public float[] GetEntityData(int maxTargets = 3)
    {
        // 每個目標包含：距離(1), 方向(2), 敵友標籤(1), 主要元素特徵(1) = 5 個 float
        float[] entityData = new float[maxTargets * 10];

        // 獲取範圍內所有 Collider
        Collider[] targets = Physics.OverlapSphere(transform.position, MaxPerceptionRange, agentLayerMask);

        // 排序並過濾出最近的實體 (略過自己)
        // 這裡需實作過濾邏輯...

        for (int i = 0; i < maxTargets; i++)
        {
            int offset = i * 10;
            if (i < targets.Length)
            {
                //Debug.Log($"感知到實體: {targets[i].name} at distance {Vector3.Distance(transform.position, targets[i].transform.position)}");
                Transform t = targets[i].transform;
                Rigidbody tRb = targets[i].GetComponent<Rigidbody>();
                // 基礎數據(0~4)
                Vector3 dir = targets[i].transform.position - transform.position;
                entityData[offset] = dir.magnitude / MaxPerceptionRange; // 歸一化距離
                entityData[offset + 1] = dir.normalized.x;
                entityData[offset + 2] = dir.normalized.y;
                entityData[offset + 3] = dir.normalized.z;
                entityData[offset + 4] = GetRelation(targets[i]);  // 敵友判斷 (同門派為正, 異門為負)
                entityData[offset + 5] = targets[i].CompareTag("Manual") ? 1f : 0f;  // 類型 (是生命體還是秘笈)
                // 5-7: 相對速度 (對手速度 - 我的速度)
                Vector3 relativeVel = (tRb != null) ? (tRb.linearVelocity - rb.linearVelocity) : (-rb.linearVelocity);
                entityData[offset + 6] = relativeVel.x;
                entityData[offset + 7] = relativeVel.y;
                entityData[offset + 8] = relativeVel.z;

                // 8: 相對尺寸 (對手尺寸 / 我的尺寸)
                // >1 代表對方比我大 (威脅), <1 代表對方比我小
                entityData[offset + 9] = t.localScale.x / transform.localScale.x;
            }
            else
            {
                // 沒有實體填充默認值
                for (int j = 0; j < 10; j++) entityData[offset + j] = 0f;
            }
        }
        //偵錯用，之後刪除
        for (int i = 0; i < entityData.Length; i++)
        {
            if (float.IsNaN(entityData[i]) || float.IsInfinity(entityData[i]))
            {
                Debug.LogError($"在索引 {i} 處發現 NaN 或 Infinity！");
                entityData[i] = 0f; // 發現 NaN 直接歸零，防止毒死模型
            }
        }
        return entityData;
    }

    // 自身狀態數據 (Internal State)
    public float[] GetSelfData()
    {
        // 包含：丹田儲存(5), 回復閾值/最大容量(2), 超載狀態與倒數(2), 熔爐狀態(5+2), 反噬債務(1)
        // 總共約 17 個 float
        float[] selfData = new float[23];

        for (int i = 0; i < 5; i++) selfData[i] = energySystem.storedElements[i] / energySystem.maxCapacity;
        selfData[5] = energySystem.regenThreshold / energySystem.maxCapacity;
        selfData[6] = energySystem.maxCapacity / 200f; // 歸一化

        selfData[7] = energySystem.isOverloaded ? 1f : 0f;
        selfData[8] = energySystem.overloadTimer / 10f; // 假設上限 10 秒

        for (int i = 0; i < 5; i++) selfData[9 + i] = furnace.furnaceElements[i];
        selfData[14] = furnace.currentHeat / furnace.heatThreshold;
        selfData[15] = furnace.activeTypeCount / 2f;

        selfData[16] = energySystem.totalOverloadDebt; // 反噬債務

        selfData[17] = rb.linearVelocity.x;  // 自身速度向量 (歸一化或原始值)
        selfData[18] = rb.linearVelocity.y;
        selfData[19] = rb.linearVelocity.z;

        selfData[20] = transform.localScale.x;  // 自身尺寸 (半徑或平均縮放)

        selfData[21] = MaxPerceptionRange / 20f;  // 自身尺標
        selfData[22] = magicStats.maxPushDistance / 50f;

        //偵錯用，之後刪除
        for (int i = 0; i < selfData.Length; i++)
        {
            if (float.IsNaN(selfData[i]) || float.IsInfinity(selfData[i]))
            {
                if (i < 4)
                    Debug.LogError($"在索引 {i} 處發現 NaN 或 Infinity！storedElements:" + energySystem.storedElements[i] + "，maxCapacity:" + energySystem.maxCapacity);
                else
                    Debug.LogError($"在索引 {i} 處發現 NaN 或 Infinity！");
                selfData[i] = 0f; // 發現 NaN 直接歸零，防止毒死模型
            }
        }
        

        return selfData;
    }

    public float GetRelation(Collider target)
    {
        if (target == null) { return 0f; }
        AgentMagic targetMagitStats = target.GetComponent<AgentMagic>();
        // 將角度轉為弧度並計算餘弦值
        float rad = (targetMagitStats.currentFrequency - magicStats.currentFrequency) * Mathf.Deg2Rad;
        float match = Mathf.Cos(rad);
        // 結果：1 = 完全一致, -1 = 完全對立, 0 = 中立
        return match;
    }
}