using UnityEngine;

public class InternalFurnace : MonoBehaviour
{
    [Header("熔爐數據結構")]
    public float[] furnaceElements = new float[5]; // 紀錄五行元素量
    public int activeTypeCount = 0;                // 當前熔爐內的元素種類數量

    [Header("熱量與損耗")]
    public float currentHeat = 0f;
    public float heatThreshold = 100f;
    public float coolingRate = 15f;
    public float baseLossRate = 0.05f;

    private InternalEnergySystem energySystem;
    private AgentMagic magic;
    private EvolutionSystem evolution;
    private AgentVisualizer visualizer;

    void Start()
    {
        energySystem = GetComponent<InternalEnergySystem>();
        magic = GetComponent<AgentMagic>();
        evolution = GetComponent<EvolutionSystem>();
        visualizer = GetComponent<AgentVisualizer>();
    }

    void Update()
    {
        // 1. 如果熔爐內有元素，進行持續反應與損耗計算
        if (activeTypeCount > 0)
        {
            ApplyFurnacePhysics();
            HandleHeatAndLoss();
        }
        else
        {
            // 無元素時強制冷卻
            currentHeat = Mathf.Max(0, currentHeat - coolingRate * Time.deltaTime);
        }
        // 取得 Visualizer 並更新
        if (visualizer != null)
        {
            visualizer.UpdateStatus(
                       magic.currentLingli / magic.maxLingli,
                       currentHeat / heatThreshold,
                       evolution != null && evolution.isEvolving
            );
        }
    }

    // --- 核心邏輯：注入元素 ---
    public void InjectElement(GridManager.ElementType type, float amount)
    {
        int index = (int)type;

        // 檢查是否超過兩種元素限制
        if (activeTypeCount >= 2 && furnaceElements[index] <= 0)
        {
            //Debug.Log("熔爐已滿，無法注入新種類元素");
            return;
        }

        // 從儲存量扣除並存入熔爐
        if (energySystem.ConsumeEnergy(index, amount))
        {
            if (furnaceElements[index] <= 0) activeTypeCount++;
            furnaceElements[index] += amount;
            currentHeat += 5f; // 注入動作產生少量瞬間熱量
        }
    }

    // --- 模擬 GridManager 的反應邏輯 ---
    private void ApplyFurnacePhysics()
    {
        // 模仿 GridManager 的反應調用
        float[] e = furnaceElements;

        // 【相生】能量轉化：生成的留下，來源的退回儲存量
        Transfer(e, GridManager.ElementType.Wood, GridManager.ElementType.Fire, 0.2f);
        Transfer(e, GridManager.ElementType.Fire, GridManager.ElementType.Earth, 0.2f);
        Transfer(e, GridManager.ElementType.Earth, GridManager.ElementType.Metal, 0.2f);
        Transfer(e, GridManager.ElementType.Metal, GridManager.ElementType.Water, 0.2f);
        Transfer(e, GridManager.ElementType.Water, GridManager.ElementType.Wood, 0.2f);

        // 【相剋】能量損耗：剋人的留下，被剋的退回儲存量
        Counter(e, GridManager.ElementType.Water, GridManager.ElementType.Fire, 0.5f);
        Counter(e, GridManager.ElementType.Fire, GridManager.ElementType.Metal, 0.5f);
        Counter(e, GridManager.ElementType.Metal, GridManager.ElementType.Wood, 0.5f);
        Counter(e, GridManager.ElementType.Wood, GridManager.ElementType.Earth, 0.5f);
        Counter(e, GridManager.ElementType.Earth, GridManager.ElementType.Water, 0.5f);

        UpdateActiveTypeCount();
    }

    // 相生：來源元素 (from) 減少並「退回」，目標元素 (to) 增加並「留下」
    private void Transfer(float[] e, GridManager.ElementType from, GridManager.ElementType to, float rate)
    {
        int fIdx = (int)from;
        int tIdx = (int)to;
        if (e[fIdx] <= 0 || e[tIdx] <= 0) return;

        float amountToTransfer = e[fIdx] * rate * Time.deltaTime;

        e[fIdx] -= amountToTransfer;
        e[tIdx] += amountToTransfer * 1.5f; // 轉化增益，留下的更多

        ReturnToStorage(from, amountToTransfer); // 來源退回儲存量
    }

    // 相剋：強者 (attacker) 留下，弱者 (defender) 減少並「退回」
    private void Counter(float[] e, GridManager.ElementType attacker, GridManager.ElementType defender, float power)
    {
        int aIdx = (int)attacker;
        int dIdx = (int)defender;
        if (e[aIdx] <= 0 || e[dIdx] <= 0) return;

        float loss = Mathf.Min(e[dIdx], e[aIdx] * power * Time.deltaTime);

        e[dIdx] -= loss;
        // 被剋制的元素退回儲存量
        ReturnToStorage(defender, loss);
    }

    // --- 收回、留下與溢出處理 ---
    public void RecallAll()
    {
        for (int i = 0; i < 5; i++)
        {
            if (furnaceElements[i] > 0)
            {
                ReturnToStorage((GridManager.ElementType)i, furnaceElements[i]);
                furnaceElements[i] = 0;
            }
        }
        activeTypeCount = 0;
    }

    private void ReturnToStorage(GridManager.ElementType type, float amount)
    {
        int index = (int)type;
        float current = energySystem.storedElements[index];
        float space = energySystem.maxCapacity - current;

        // 超過最大儲存量則自動刪除（不進行任何操作即為刪除）
        if (space > 0)
        {
            energySystem.storedElements[index] += Mathf.Min(amount, space);
        }
    }

    private void HandleHeatAndLoss()
    {
        // 如果有兩種元素正在反應，熱量大幅增加
        if (activeTypeCount >= 2)
        {
            currentHeat += Time.deltaTime * 25f;
        }
        else
        {
            currentHeat = Mathf.Max(0, currentHeat - coolingRate * Time.deltaTime);
        }

        // 過熱損耗：熱量越高，熔爐內的元素揮發越快
        float heatFactor = currentHeat / heatThreshold;
        float totalLossRate = baseLossRate * (1f + heatFactor * 5f);

        for (int i = 0; i < 5; i++)
        {
            furnaceElements[i] = Mathf.Max(0, furnaceElements[i] - furnaceElements[i] * totalLossRate * Time.deltaTime);
        }
        // 重新檢查是否還有元素   
        activeTypeCount = 0;
        for (int i = 0; i < 5; i++)
        {
            if (furnaceElements[i] > 0.001f) activeTypeCount++;
        }
    }

    private void UpdateActiveTypeCount()
    {
        int count = 0;
        for (int i = 0; i < 5; i++)
        {
            if (furnaceElements[i] > 0.001f) count++;
            else furnaceElements[i] = 0;
        }
        activeTypeCount = count;
    }

    // 外部接口
    public void AddHeat(float heatAdd)
    {
        currentHeat += heatAdd;
    }
}