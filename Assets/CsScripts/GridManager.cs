using System.Collections.Generic;
using UnityEngine;
using static GridManager;

public partial class GridManager : MonoBehaviour
{
    public static GridManager _instance; // 建立靜態實例

    public enum ElementType { Metal = 0, Wood = 1, Water = 2, Fire = 3, Earth = 4, None = 5 }

    [System.Serializable]
    public struct Cell
    {
        public float[] elements;
        public float frequency;    // 頻率 (0-360)，代表身分印記
        public float controlValue; // 掌控度 (0-1)，代表意志注入強度
        public Vector3 velocity;
        public float scent;  // 氣味維度
        public float terrainType;

        public Cell(int count)
        {
            elements = new float[count];
            frequency = 0; //Random.Range(0f, 360f);
            controlValue = 0;
            velocity = Vector3.zero;
            scent = 0;
            terrainType = 0;
        }
    }

    [Header("3D 網格規模")]
    public int xSize = 60;
    public int ySize = 1;  // 目前設為 1，即為單層平面
    public int zSize = 60;
    public float cellSize = 1.0f;

    [Header("五行物理參數")]  //順序：金木水火土
    // 流動性 (Viscosity): 數值越小流動越快
    public float[] viscosities = { 0.9f, 0.4f, 0.1f, 0.05f, 0.6f };  //{ 0.9f, 0.4f, 0.1f, 0.05f, 0.6f };
    // 擴散率 (Diffusion): 向四周蔓延的速度
    public float[] diffusionRates = { 0.11f, 0.19f, 0.25f, 0.4f, 0.15f };  //{ 0.01f, 0.05f, 0.15f, 0.3f, 0.02f }
    // 衰減率 (Decay): 能量隨時間消散的速度
    public float[] decayRates = { 0.53f, 0.6f, 0.68f, 0.8f, 0.48f };  //{ 0.08f, 0.15f, 0.12f, 0.3f, 0.06f };

    [Header("氣味屬性")]
    public float viscositiesScent = 0.1f;
    public float diffusionRatesScent = 0.2f;
    public float decayRatesScent = 0.01f;

    [Header("掌控度屬性")]
    public float viscositiesControl = 0.1f;
    public float diffusionRatesControl = 0.2f;
    public float decayRatesControl = 0.01f;

    public List<MapGenerator.ManaVein> activeVeins = new List<MapGenerator.ManaVein>();

    private Cell[,,] grid; // 改為三維陣列 [X, Y, Z]
    private Cell[,,] nextGrid;
    private int[,] dir6 = { { 1, 0, 0 }, { -1, 0, 0 }, 
                            { 0, 1, 0 }, { 0, -1, 0 }, 
                            { 0, 0, 1 }, { 0, 0, -1 } };
    private int[,] side12 = { { 1, 1, 0 }, { -1, 1, 0 }, { 1, -1, 0 }, { -1, -1, 0 }, 
                              { 1, 0, 1 }, { -1, 0, 1 }, { 1, 0, -1 }, { -1, 0, -1 }, 
                              { 0, 1, 1 }, { 0, -1, 1 }, { 0, 1, -1 }, { 0, -1, -1 }};
    private int[,] corner8 = { {  1, 1, 1 }, {  1, 1, -1 }, {  1, -1, 1 }, {  1, -1, -1 },
                               { -1, 1, 1 }, { -1, 1, -1 }, { -1, -1, 1 }, { -1, -1, -1 }};

    [Header("計算頻率設定")]
    private int frameSkip = 0;
    public int updateInterval = 1; // 每 5 個物理幀才更新一次格點
    private float timeAccumulator = 0f;

    // 修改屬性 Getter，加入自動尋找機制
    public static GridManager Instance
    {
        get
        {
            // 如果別人呼叫時 _instance 是空的（可能它的 Awake 還沒執行）
            if (_instance == null)
            {
                // 主動去場景中尋找
                _instance = FindAnyObjectByType<GridManager>();

                if (_instance == null)
                {
                    Debug.LogError("場景中完全找不到 GridManager！請確認是否有放置該物件。");
                }
            }
            return _instance;
        }
    }
    void Awake()
    {
        // 確保場景中只有一個 GridManager
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            _instance = this;
        }
        grid = new Cell[xSize, ySize, zSize];
        nextGrid = new Cell[xSize, ySize, zSize];
        InitializeGrid();
    }

    private void Start()
    {
        activeVeins = FindAnyObjectByType<MapGenerator>().veins; // 從 MapGenerator 獲取魔脈列表
    }

    void InitializeGrid()
    {
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    grid[x, y, z] = new Cell(5);
                    nextGrid[x, y, z] = new Cell(5);
                }
            }
        }
    }

    void FixedUpdate()
    {
        frameSkip++;
        timeAccumulator += Time.fixedDeltaTime; // 累積跳過的時間
        if (frameSkip >= updateInterval)
        {
            // 因為我們跳過了幀，所以要把流逝的時間補回來傳進去 (0.02 * 5 = 0.1秒)
            float simulatedDeltaTime = timeAccumulator;
            timeAccumulator = 0f; // 重置累積器
            HandleEnergyRegeneration(simulatedDeltaTime); // 4. 處理魔脈能量再生
            SimulateAdvection(simulatedDeltaTime);        // 1. 處理向量遷移 (流動)
            SimulateDiffusion(simulatedDeltaTime);        // 2. 處理自然擴散
            ApplyFiveElements(simulatedDeltaTime);        // 3. 處理相生相剋
            SwapBuffers();
            frameSkip = 0;
        }
    }

    // --- 向量遷移 (3D 邏輯) ---
    void SimulateAdvection(float simulatedDeltaTime)
    {
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    Vector3 vel = grid[x, y, z].velocity;
                    if (vel.sqrMagnitude < 0.001f) continue;

                    // 3D 座標偏移計算
                    float targetX = x + vel.x * simulatedDeltaTime;
                    float targetY = y + vel.y * simulatedDeltaTime;
                    float targetZ = z + vel.z * simulatedDeltaTime;

                    // 邊界檢查
                    int tx = Mathf.Clamp(Mathf.RoundToInt(targetX), 0, xSize - 1);
                    int ty = Mathf.Clamp(Mathf.RoundToInt(targetY), 0, ySize - 1);
                    int tz = Mathf.Clamp(Mathf.RoundToInt(targetZ), 0, zSize - 1);

                    float moveAmount;
                    for (int i = 0; i < 5; i++)
                    {
                        moveAmount = grid[x, y, z].elements[i] * (1.0f - viscosities[i]);
                        nextGrid[tx, ty, tz].elements[i] += moveAmount;
                        nextGrid[x, y, z].elements[i] -= moveAmount;
                    }
                    moveAmount = grid[x, y, z].scent * (1.0f - viscositiesScent);
                    nextGrid[tx, ty, tz].scent += moveAmount;
                    nextGrid[x, y, z].scent -= moveAmount;

                    nextGrid[x, y, z].velocity *= 0.9f;
                }
            }
        }
    }

    // --- 2. 自然擴散與衰減 ---
    void SimulateDiffusion(float simulatedDeltaTime)
    {
        // 預先計算 cellSize 的平方，避免在迴圈內重複計算
        float sqrCellSize = cellSize * cellSize;
        
        // 衰減
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        float decayed = grid[x, y, z].elements[i] * Mathf.Exp(-(decayRates[i]+0.1f) * simulatedDeltaTime);
                        nextGrid[x, y, z].elements[i] = (decayed < 0.01f) ? 0 : decayed;
                        if (x == 30 && y == 0 && z == 30) // 調試輸出第一個格點的元素衰減情況
                        {
                            Debug.Log($"格點 [30,0,30] 的元素 {i} 從 {grid[x, y, z].elements[i]:F4} 衰減到 {nextGrid[x, y, z].elements[i]:F4}，衰減倍率：{Mathf.Exp(-(decayRates[i]+0.1f) * simulatedDeltaTime)}");
                        }
                    }
                    float scentDecayed = grid[x, y, z].scent * Mathf.Exp(-decayRatesScent * simulatedDeltaTime);
                    nextGrid[x, y, z].scent = (scentDecayed < 0.1f) ? 0 : scentDecayed;

                    float ctrlDecayed = grid[x, y, z].controlValue * Mathf.Exp(-decayRatesControl * simulatedDeltaTime);
                    nextGrid[x, y, z].controlValue = (ctrlDecayed < 0.01f) ? 0 : ctrlDecayed;
                }
            }
        }
        
        // 擴散（從內部向外擴散，避免同一輪中重複影響）
        bool xOmitEdge = (xSize > 2);
        bool yOmitEdge = (ySize > 2);
        bool zOmitEdge = (zSize > 2);
        bool needBoundCheck = ((!xOmitEdge) || (!yOmitEdge) || (!zOmitEdge)); // 如果任一維度需要忽略邊界，則啟用邊界檢查
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    if (((x <= 0 || x >= xSize - 1) && xOmitEdge) || ((y <= 0 || y >= ySize - 1) && yOmitEdge) || ((z <= 0 || z >= zSize - 1) && zOmitEdge))
                    {
                        // 邊界格點不擴散，直接跳過
                        continue;
                    }
                    for (int i = 0; i < 5; i++)
                    {

                        float current = grid[x, y, z].elements[i];
                        
                        if (current > 0.001f)  // 能量太低不擴散，節省效能
                        {
                            // 乘上 DeltaTime 確保擴散是平滑的
                            float diffRate = ((diffusionRates[i]) / sqrCellSize) * simulatedDeltaTime;
                            // 安全鎖：防止流出的總能量超過自己擁有的 100% (19.16 是總權重)
                            if (diffRate * 19.16f > 1.0f) diffRate = 1.0f / 19.16f;
                            for (int j = 0; j < 6; j++)
                            {
                                int newx = x + dir6[j, 0];
                                int newy = y + dir6[j, 1];
                                int newz = z + dir6[j, 2];
                                if (needBoundCheck)
                                {
                                    if (!IsInBounds(newx, newy, newz))
                                        continue; // 跳過越界的鄰居
                                }
                                float flow = current * diffRate;
                                nextGrid[x, y, z].elements[i] -= flow;          // 扣自己
                                nextGrid[newx, newy, newz].elements[i] += flow; // 給鄰居
                            }
                            for (int j = 0; j < 12; j++)
                            {
                                int newx = x + side12[j, 0];
                                int newy = y + side12[j, 1];
                                int newz = z + side12[j, 2];
                                if (needBoundCheck)
                                {
                                    if (!IsInBounds(newx, newy, newz))
                                        continue; // 跳過越界的鄰居
                                }
                                float flow = current * diffRate * 0.71f; // 1/根號2
                                nextGrid[x, y, z].elements[i] -= flow;
                                nextGrid[newx, newy, newz].elements[i] += flow;
                            }
                            for (int j = 0; j < 8; j++)
                            {
                                int newx = x + corner8[j, 0];
                                int newy = y + corner8[j, 1];
                                int newz = z + corner8[j, 2];
                                if (needBoundCheck)
                                {
                                    if (!IsInBounds(newx, newy, newz))
                                        continue; // 跳過越界的鄰居
                                }
                                float flow = current * diffRate * 0.58f; // 1/根號3
                                nextGrid[x, y, z].elements[i] -= flow;
                                nextGrid[newx, newy, newz].elements[i] += flow;
                            }
                        }
                    }
                    // 擴散
                    float curScent = grid[x, y, z].scent;
                    float curCtrl = grid[x, y, z].controlValue;
                    float scentRate = Mathf.Min((diffusionRatesScent / sqrCellSize) * simulatedDeltaTime, 1f / 19.16f);
                    float ctrlRate = Mathf.Min((diffusionRatesControl / sqrCellSize) * simulatedDeltaTime, 1f / 19.16f);
                    if (curScent > 0.001f || curCtrl > 0.001f)
                    {
                        for (int j = 0; j < 6; j++)
                        {
                            int newx = x + dir6[j, 0];
                            int newy = y + dir6[j, 1];
                            int newz = z + dir6[j, 2];
                            if (needBoundCheck)
                            {
                                if (!IsInBounds(newx, newy, newz))
                                    continue; // 跳過越界的鄰居
                            }
                            float fScent = curScent * scentRate;
                            float fCtrl = curCtrl * ctrlRate;
                            nextGrid[x, y, z].scent -= fScent;
                            nextGrid[newx, newy, newz].scent += fScent;
                            nextGrid[x, y, z].controlValue -= fCtrl;
                            nextGrid[newx, newy, newz].controlValue += fCtrl; 
                        }
                        for (int j = 0; j < 12; j++)
                        {
                            int newx = x + side12[j, 0];
                            int newy = y + side12[j, 1];
                            int newz = z + side12[j, 2];
                            if (needBoundCheck)
                            {
                                if (!IsInBounds(newx, newy, newz))
                                    continue; // 跳過越界的鄰居
                            }
                            float fScent = curScent * scentRate * 0.71f; // 1/根號2
                            float fCtrl = curCtrl * ctrlRate * 0.71f;
                            nextGrid[x, y, z].scent -= fScent;
                            nextGrid[newx, newy, newz].scent += fScent;
                            nextGrid[x, y, z].controlValue -= fCtrl;
                            nextGrid[newx, newy, newz].controlValue += fCtrl;
                        }
                        for (int j = 0; j < 8; j++)
                        {
                            int newx = x + corner8[j, 0];
                            int newy = y + corner8[j, 1];
                            int newz = z + corner8[j, 2];
                            if (needBoundCheck)
                            {
                                if (!IsInBounds(newx, newy, newz))
                                    continue; // 跳過越界的鄰居
                            }
                            float fScent = curScent * scentRate * 0.58f; // 1/根號3
                            float fCtrl = curCtrl * ctrlRate * 0.58f;
                            nextGrid[x, y, z].scent -= fScent;
                            nextGrid[newx, newy, newz].scent += fScent;
                            nextGrid[x, y, z].controlValue -= fCtrl;
                            nextGrid[newx, newy, newz].controlValue += fCtrl;
                        }
                    }
                }
            }
        }
    }

    // --- 相生相剋 (3D 循環) ---
    void ApplyFiveElements(float simulatedDeltaTime)
    {
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    // 此處調用先前的 Transfer 與 Counter 邏輯
                    Cell cell = nextGrid[x, y, z];
                    float[] e = cell.elements;
                    float ctrl = cell.controlValue; // 取得該格的掌控度

                    // --- 【相生】 --- 
                    // 只有在掌控度高 (有人操作) 或 能量極高 (自然溢流) 時才觸發
                    // 基礎轉化率設低 (例如 0.01)，受 control 影響後提升
                    Transfer(e, ElementType.Wood, ElementType.Fire, 0.1f, ctrl, simulatedDeltaTime);
                    Transfer(e, ElementType.Fire, ElementType.Earth, 0.08f, ctrl, simulatedDeltaTime);
                    Transfer(e, ElementType.Earth, ElementType.Metal, 0.05f, ctrl, simulatedDeltaTime);
                    Transfer(e, ElementType.Metal, ElementType.Water, 0.1f, ctrl, simulatedDeltaTime);
                    Transfer(e, ElementType.Water, ElementType.Wood, 0.12f, ctrl, simulatedDeltaTime);

                    // --- 【相剋】 ---
                    // 相剋通常代表衝突，可以設定為與「掌控度」相反
                    // 如果兩者頻率不同且掌控度低，反應較劇烈；如果有強大意志壓制，反應變慢
                    Counter(e, ElementType.Water, ElementType.Fire, 0.5f, ctrl, simulatedDeltaTime);
                    Counter(e, ElementType.Fire, ElementType.Metal, 0.4f, ctrl, simulatedDeltaTime);
                    Counter(e, ElementType.Metal, ElementType.Wood, 0.6f, ctrl, simulatedDeltaTime);
                    Counter(e, ElementType.Wood, ElementType.Earth, 0.3f, ctrl, simulatedDeltaTime);
                    Counter(e, ElementType.Earth, ElementType.Water, 0.4f, ctrl, simulatedDeltaTime);
                }
            }
        }
    }

    // 相生輔助函式
    void Transfer(float[] e, ElementType from, ElementType to, float baseRate, float control, float dt)
    {
        float srcAmount = e[(int)from];
        if (srcAmount <= 0.001f) return;

        // 1. 計算活化係數
        // 自然溢流門檻 (例如濃度 > 80 才會開始自動轉化)
        float naturalOverflow = Mathf.Max(0, srcAmount - 80f) * 0.05f;

        // 意志催化 (掌控度越高，轉化越快)
        float forcedTransfer = control * baseRate;

        // 總轉化率 = 極微小的自然流失 + 自然溢流 + 意志引導
        float totalRate = 0.001f + naturalOverflow + forcedTransfer;

        // 2. 「火種」邏輯：如果目標元素為 0，轉化效率降低 (難以憑空產生)
        if (e[(int)to] <= 0.01f)
        {
            totalRate *= 0.1f;
        }

        float amount = srcAmount * totalRate * dt;

        // 確保不會扣到負數
        amount = Mathf.Min(amount, srcAmount);

        e[(int)from] -= amount;
        float conversionEfficiency = 0.1f + (control * 0.3f);
        e[(int)to] += amount * conversionEfficiency;
        // 【核心改動：修仙熱力學】
        // 轉化過程中會有大量的能量逸散（變成無法利用的廢熱/自然消散）
        // 基礎轉化率只有 10%。就算生命體注入最強的意志 (control=1)，最高也只有 40% 的效率
        // 生成的目標元素被大幅削減
        //e[(int)to] += amount * 0.95f; //1.2f
    }

    // 相剋輔助函式
    void Counter(float[] e, ElementType attacker, ElementType defender, float power, float control, float dt)
    {
        float a = e[(int)attacker];
        float d = e[(int)defender];

        if (a > 0.01f && d > 0.01f)
        {
            // 意志力越高，越能壓制五行衝突 (相剋反應變慢)
            // 意志力低時，衝突完全爆發
            float suppression = 1.0f - Mathf.Clamp(control, 0, 0.9f);

            float damage = Mathf.Min(a * power * suppression, d) * dt * 5.0f;

            e[(int)defender] -= damage;
            e[(int)attacker] -= damage * 0.8f;// 攻擊元素也會受到近乎 1:1 的劇烈折損
        }
    }

    // --- 外部接口 (Vector3 座標轉換) ---

    // 生命體向格點注入速度 (施法方向)
    public void AddImpulse(Vector3 worldPos, Vector3 force, ElementType type, float amount)
    {
        int x = Mathf.FloorToInt(worldPos.x / cellSize);
        int y = Mathf.FloorToInt(worldPos.y / cellSize); // 加入 Y 軸判斷
        int z = Mathf.FloorToInt(worldPos.z / cellSize);

        if (IsInBounds(x, y, z))
        {
            grid[x, y, z].velocity += force;
            grid[x, y, z].elements[(int)type] += amount;
        }
    }

// 生命體吸收元素
    public float SiphonElement(Vector3 worldPos, ElementType type, float requestAmount)
    {
        int x = Mathf.FloorToInt(worldPos.x / cellSize);
        int y = Mathf.FloorToInt(worldPos.y / cellSize);
        int z = Mathf.FloorToInt(worldPos.z / cellSize);
        if (IsInBounds(x, y, z))
        {
            float available = grid[x, y, z].elements[(int)type];
            float taken = Mathf.Min(available, requestAmount);
            grid[x, y, z].elements[(int)type] -= taken;
            return taken;
        }
        return 0;
    }
    // 生命體偵測回報
    public Cell GetElementAt(int x, int y, int z, bool verify = false)
    {
        
        if (IsInBounds(x, y, z) && !(grid == null))
        {
            Cell cell = grid[x, y, z];
            if (float.IsNaN(cell.elements[0]) || float.IsInfinity(cell.elements[0]) || cell.elements[0]<0)
            {
                Debug.LogError($"網格 [{x},{y},{z}] 的數據已經崩潰成 NaN！elements=" + cell.elements + "，elements[0]=" + cell.elements[0]);
            }
            return cell;
        }
        if (verify)
        {
            Debug.LogWarning($"嘗試訪問無效的格點 [{x},{y},{z}]，返回空元素。");
        }
        return new Cell(5); // 返回空元素
    }
    public Cell GetElementAt(Vector3 samplePos, bool verify = false)
    {
        return GetElementAt(Mathf.RoundToInt(samplePos.x), Mathf.RoundToInt(samplePos.y), Mathf.RoundToInt(samplePos.z), verify);
    }

    // 判定格點頻率與生命體頻率的匹配程度
    public float GetFrequencyMatch(float gridFreq, float agentFreq, float gridControl)
    {
        // 將角度轉為弧度並計算餘弦值
        float rad = (gridFreq - agentFreq) * Mathf.Deg2Rad;
        float match = Mathf.Cos(rad) * gridControl;
        // 結果：1 = 完全一致, -1 = 完全對立, 0 = 中立
        return match;
    }
    public float GetFrequencyMatch(float gridFreq, float agentFreq)
    {
        return GetFrequencyMatch(gridFreq, agentFreq, 1);
    }

    /*public float GetFrequencyMatch(float gridFreq, float agentFreq)
    {
        float diff = Mathf.Abs(gridFreq - agentFreq);
        if (diff > 180) diff = 360 - diff;
        // 映射到 1 (完全相同) 到 -1 (完全相反)
        float match = Mathf.Cos(diff * Mathf.Deg2Rad);
        return match;
    }*/

    public bool IsInBounds(int x, int y, int z)
    {
        return x >= 0 && x < xSize && y >= 0 && y < ySize && z >= 0 && z < zSize;
    }

    public bool IsInBounds(Vector3 samplePos)
    {
        return IsInBounds(Mathf.RoundToInt(samplePos.x), Mathf.RoundToInt(samplePos.y), Mathf.RoundToInt(samplePos.z));
    }
    
    void SwapBuffers()
    {
        // 在三維陣列中，Array.Copy 依然有效
        //System.Array.Copy(nextGrid, grid, nextGrid.Length);

        float absoluteMaxEnergy = 1000f; // 天道限制：單一網格單一元素的最大極限
        for (int x = 0; x < xSize; x++)
        {
            for (int y = 0; y < ySize; y++)
            {
                for (int z = 0; z < zSize; z++)
                {
                    for (int i = 0; i < 5; i++)
                    {
                        // 強制鎖住上限，防止任何指數爆炸產生 Infinity
                        grid[x, y, z].elements[i] = Mathf.Clamp(nextGrid[x, y, z].elements[i], 0f, absoluteMaxEnergy);

                        // 清空 nextGrid 供下一幀使用
                        nextGrid[x, y, z].elements[i] = grid[x, y, z].elements[i];
                    }

                    grid[x, y, z].scent = nextGrid[x, y, z].scent;
                    grid[x, y, z].controlValue = nextGrid[x, y, z].controlValue;
                    grid[x, y, z].frequency = nextGrid[x, y, z].frequency;
                    grid[x, y, z].velocity = nextGrid[x, y, z].velocity;
                }
            }
        }
    }

    public void InjectAtCell(int x, int y, int z, ElementType type, float amount)
    {
        if (IsInBounds(x, y, z))
        {
            grid[x, y, z].elements[(int)type] += amount;
        }
    }

    // 在 Update 的物理循環中加入
    private void HandleEnergyRegeneration(float simulatedDeltaTime)
    {
        foreach (var vein in activeVeins)
        {
            // 每幀在魔脈位置注入微量元素
            // 这里的 UpdateControl 可以讓魔脈自動向原生頻率靠攏，除非被 AI 強行改變
            //UpdateControl(vein.position, vein.radius, vein.frequency, 0.01f);
            //Debug.Log($"魔脈補充：{vein.name} 於 {vein.position}");
            // 注入元素
            if (GetElementAt(vein.position).elements[(int)vein.mainElement] < 500)
            {
                AddImpulseSphere(
                vein.position,
                vein.radius,
                0.5f,
                Vector3.zero,
                vein.mainElement,
                vein.flowIntensity * simulatedDeltaTime * 10,
                vein.frequency,
                0.01f
            );
            } else {
                //Debug.Log($"魔脈 {vein.name} 的元素已經超過 5，暫停注入，讓它自然流動和被玩家互動。");
                /*
                // 如果元素已經超過一定量，則注入頻率和掌控度，讓它更穩定
                AddImpulseSphere(
                vein.position,
                vein.radius,
                0.5f,
                Vector3.zero,
                ElementType.None, // 不再注入元素
                0,
                vein.frequency,
                0.01f
            );
                */
            }

            //AddImpulseSphere(vein.position, vein.radius, 0.8f, Vector3.zero, vein.mainElement, 50f, vein.frequency, 0.5f);
        }
    }
}


public partial class GridManager : MonoBehaviour // 使用 partial 方便擴充
{
    [Header("隨機擾動係數")]
    public float globalRandomScale = 0.2f;

    /// <summary>
    /// 遠程靈力注入：更新格點掌控度與頻率
    /// </summary>
    public void UpdateControl(Vector3 worldPos, float radius, float agentFreq, float gain)
    {
        ExecuteInSphere(worldPos, radius, (int x, int y, int z, float linearWeight) => {
            float normDist = 1f - linearWeight;
            // 這裡同樣建議套用 Falloff，否則邊緣同化會太硬
            float weight = CalculateFalloff(normDist, 0.5f);

            float match = GetFrequencyMatch(grid[x, y, z].frequency, agentFreq);

            if (match > 0.7f)
            {
                grid[x, y, z].frequency = agentFreq;
                grid[x, y, z].controlValue = Mathf.Clamp01(grid[x, y, z].controlValue + gain * weight);
            }
            else
            {
                grid[x, y, z].controlValue -= gain * weight;
                if (grid[x, y, z].controlValue < 0)
                {
                    grid[x, y, z].controlValue = Mathf.Abs(grid[x, y, z].controlValue);
                    grid[x, y, z].frequency = agentFreq;
                }
            }
        });
    }

    // 更新 AddImpulseSphere 支援掌控度注入
    public void AddImpulseSphere(Vector3 worldPos, float radius, float precision, Vector3 force, ElementType type, float amountPerCell, float agentFreq = 0, float controlGain = 0)
    {
        ExecuteInSphere(worldPos, radius, (int x, int y, int z, float linearWeight) => {
            // linearWeight 是 1(中) 到 0(邊)，所以 normDist(距離) 就是 1 - linearWeight
            float normDist = 1f - linearWeight;
            float finalWeight = CalculateFalloff(normDist, precision);

            // 物理推力
            grid[x, y, z].velocity += force * finalWeight;

            // 元素注入
            if (type != ElementType.None)
            {
                // 注意：這裏的 amountPerCell 建議定義為「中心點最大注入量」
                grid[x, y, z].elements[(int)type] += amountPerCell * finalWeight;
            }

            // 注入頻率與掌控 (同樣套用衰減，讓邊緣同化速度較慢)
            if (controlGain > 0)
            {
                // 混合頻率而非直接覆蓋（可選，看你的需求）
                grid[x, y, z].frequency = Mathf.Lerp(grid[x, y, z].frequency, agentFreq, finalWeight);
                grid[x, y, z].controlValue = Mathf.Clamp01(grid[x, y, z].controlValue + controlGain * finalWeight);
            }
        });
    }

    /// <summary>
    /// 以球體範圍吸收元素
    /// </summary>
    public float SiphonElementSphere(Vector3 worldPos, float radius, float precision, ElementType type, float requestAmount)
    {
        float totalSiphoned = 0;
        int elementIdx = (int)type;

        ExecuteInSphere(worldPos, radius, (x, y, z, linearWeight) =>
        {
            float normDist = 1f - linearWeight;
            float falloffWeight = CalculateFalloff(normDist, precision);

            if (falloffWeight <= 0) return;

            float available = grid[x, y, z].elements[elementIdx];
            if (available <= 0) return;

            // 根據權重決定這格能被吸多少
            // 修正：不使用硬編碼 /10f，而是根據權重分配
            float canTake = available * falloffWeight;

            // 為了防止單格吸乾，可以設一個比例上限 (例如單格一次最多吸 20%)
            float taken = Mathf.Min(canTake, requestAmount * 0.2f);

            grid[x, y, z].elements[elementIdx] -= taken;
            totalSiphoned += taken;
        });

        return totalSiphoned;
    }

    // 輔助方法：球體遍歷
    private void ExecuteInSphere(Vector3 worldPos, float radius, System.Action<int, int, int, float> action)
    {
        int centerX = Mathf.FloorToInt(worldPos.x / cellSize);
        int centerY = Mathf.FloorToInt(worldPos.y / cellSize);
        int centerZ = Mathf.FloorToInt(worldPos.z / cellSize);
        int cellRadius = Mathf.CeilToInt(radius / cellSize);
        float sqrRadius = radius * radius;

        for (int x = centerX - cellRadius; x <= centerX + cellRadius; x++)
        {
            for (int y = centerY - cellRadius; y <= centerY + cellRadius; y++)
            {
                for (int z = centerZ - cellRadius; z <= centerZ + cellRadius; z++)
                {
                    if (!IsInBounds(x, y, z)) continue;
                    Vector3 cellWorldPos = new Vector3(x * cellSize, y * cellSize, z * cellSize);
                    float sqrDist = (cellWorldPos - worldPos).sqrMagnitude;
                    if (sqrDist < sqrRadius)
                    {
                        float dist = Vector3.Distance(cellWorldPos, worldPos);
                        if (dist <= radius) action(x, y, z, 1f - (dist / radius));
                    }
                }
            }
        }
    }

    // 計算衰減與分散曲線
    private float CalculateFalloff(float normDist, float precision)
    {
        /*
        // 高精度：接近 1 的地方快速掉落 (Sharp Edge)
        // 低精度：掉落平緩，且在 normDist > 1 時仍有殘餘值 (Dispersal)
        if (precision >= 0.9f)
        {
            return normDist <= 1f ? 1f : 0f;
        }

        // 使用 Smoothstep 模擬邊界模糊
        float edgeStart = precision; // 精度決定從哪裡開始衰減
        float edgeEnd = 1f + (1f - precision);

        return 1f - Mathf.SmoothStep(0, edgeEnd, normDist);
        */
        // normDist: 0=中心, 1=邊緣
        if (precision >= 0.95f)
        {
            return normDist <= 1.0f ? 1.0f : 0f; // 幾乎是硬邊緣的實心圓
        }

        // 精度越高，核心實心區越大；精度越低，從中心就開始衰減
        float coreArea = precision * 0.8f;
        if (normDist <= coreArea) return 1.0f;

        // 在核心區與邊緣之間進行平滑衰減
        // 使用 InverseLerp 將 (coreArea ~ 1.0) 映射到 (0 ~ 1) 供 SmoothStep 使用
        float t = Mathf.InverseLerp(coreArea, 1.0f, normDist);
        return 1.0f - Mathf.SmoothStep(0f, 1f, t);
    }
}