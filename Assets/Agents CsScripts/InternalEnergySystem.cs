using UnityEngine;
using static Unity.VisualScripting.Member;

public class InternalEnergySystem : MonoBehaviour
{
    [Header("基礎屬性")]
    public float regenThreshold; // 回復儲存量 (先天決定)
    public float maxCapacity;    // 最大儲存量 (後天練習增加)
    public float stability = 0.8f;        // 穩定度：影響元素自然消散的速度

    [Header("體內元素儲存 (金, 木, 水, 火, 土)")]
    public float[] storedElements = new float[5];

    [Header("部位強化分配 (4 個部位)")]
    // 每個部位目前灌注的總能量
    public float[] partReinforcement = new float[4];

    [Header("超載與反噬系統")]
    public bool isOverloaded = false;
    public float overloadTimer = 0f;
    public float totalOverloadDebt = 0f; // 累積的反噬強度
    private float currentOverloadLimit = 0f;
    public float overloadCooldown = 0f;

    private InternalFurnace furnace;
    private AgentMagic magicStats; // 引用屬性腳本以扣除屬性

    private void Awake()
    {
        furnace = GetComponent<InternalFurnace>();
    }

    void Start()
    {
        magicStats = GetComponent<AgentMagic>();
    }

    private void Update()
    {
        // 1. 生命代謝：維持生命每秒消耗微量元素（優先消耗木或水）
        ApplyMetabolism();

        // 2. 體內五行運轉：體內元素也會互相生剋，但速度比外界慢
        ApplyInternalCycle();
        float currentTotal = GetTotalStored();

        // 冷卻時間倒數
        if (overloadCooldown > 0)
        {
            overloadCooldown -= Time.deltaTime;
        }

        if (!isOverloaded)
        {
            // 1. 自動恢復邏輯 (低於回復儲存量)
            if (currentTotal < regenThreshold)
            {
                ApplyRegeneration();
            }
            // 2. 自動消散邏輯 (介於兩者之間)
            else if (currentTotal > regenThreshold)
            {
                ApplyDissipation();
            }
        }
        else
        {
            // 3. 超載模式處理
            HandleOverload(currentTotal);
        }
    }

    // 由 AgentMagic 的 ExecutePartSiphon 呼叫
    public void AddEnergy(int typeIndex, float amount, string source = "")
    {
        float currentTotal = GetTotalStored();
        // 若超過總容量，則無法繼續吸收
        float room = Mathf.Max(0, maxCapacity - currentTotal);
        if (room >= amount)
        {
            storedElements[typeIndex] += amount;
        }
        else
        {
            storedElements[typeIndex] = maxCapacity;
            furnace.AddHeat((amount - room) * 0.2f);
        }
        if (!gameObject.name.Contains("("))
        {
            //Debug.Log($"{gameObject.name} 獲得靈力 {amount:F2} 點，剩餘靈力: {GetTotalStored():F2}，來源: {source}");
        }
    }

    // 由 AgentMagic 的 ExecutePartCast 呼叫
    public bool ConsumeEnergy(int typeIndex, float amount, string source = "")
    {
        if (!gameObject.name.Contains("("))
        {
            //Debug.Log($"{gameObject.name} 使用靈力 {amount:F2} 點，剩餘靈力: {GetTotalStored():F2}，來源: {source}");
        }
        if (storedElements[typeIndex] >= amount)
        {
            storedElements[typeIndex] -= amount;
            return true;
        }
        return false; // 能量不足，施法失敗（或導致反噬）
    }

    private void ApplyMetabolism()
    {
        float decay = (1f - stability) * 0.05f * Time.deltaTime;
        for (int i = 0; i < 5; i++)
        {
            storedElements[i] = Mathf.Max(0, storedElements[i] - decay);
        }

        // 基礎生存消耗：每秒消耗微量生機 (木) 或 滋潤 (水)
        float survivalCost = 2.0f * Time.deltaTime;
        int wood = (int)GridManager.ElementType.Wood;
        int water = (int)GridManager.ElementType.Water;

        bool hasEnergy = false;
        if (storedElements[wood] >= survivalCost)
        {
            storedElements[wood] -= survivalCost;
            hasEnergy = true;
        }
        else if (storedElements[water] >= survivalCost)
        {
            storedElements[water] -= survivalCost;
            hasEnergy = true;
        }

        if (!hasEnergy)
        {
            // 缺乏靈氣滋養，肉身開始衰敗 (扣血)
            HealthSystem health = GetComponent<HealthSystem>();
            if (health != null) health.TakeDamage(survivalCost * 2.0f, "缺乏靈力");
        }
    }

    private void ApplyInternalCycle()
    {
        // 體內生剋：這裡可以實作微弱的「生」
        // 例如：體內有水，會緩慢自動轉化為木，這就是「滋養」
    }

    // 自動恢復 (基於某種屬性，如 木 屬性)
    private void ApplyRegeneration()
    {
        // 安全檢查：確保精度不會變成負數導致「負恢復」
        float effectivePrecision = Mathf.Max(0.1f, magicStats.precision);
        float regenSpeed = effectivePrecision * 0.5f; // 假設與精度相關
        if (GetTotalStored() < regenThreshold)
        {
            for (int i = 0; i < 5; i++)
            {
                storedElements[i] += regenSpeed * Time.deltaTime * 0.2f;
            } 
        }
    }

    // 自動消散
    private void ApplyDissipation()
    {
        float lossRate = 0.1f; // 消散速度
        for (int i = 0; i < 5; i++)
        {
            storedElements[i] = Mathf.Lerp(storedElements[i], 0, lossRate * Time.deltaTime);
            // 這裡可以加入邏輯確保總量不低於 regenThreshold
        }
    }

    // 啟動超載 (爆種)
    public void TriggerOverload(float plannedSacrifice)
    {
        if (isOverloaded || overloadCooldown > 0) return;

        isOverloaded = true;
        // 時長由願犧牲的屬性點決定 (例如 1 點屬性換 5 秒)
        currentOverloadLimit = plannedSacrifice * 5.0f;
        overloadTimer = 0f;
        totalOverloadDebt = plannedSacrifice;

        // 臨時增加施法力量與精度
        magicStats.power *= 1.5f;
        magicStats.precision = Mathf.Min(1.0f, magicStats.precision * 1.2f);
    }

    private void HandleOverload(float currentTotal)
    {
        overloadTimer += Time.deltaTime;

        // 計算反噬強度：由超出的量與持續時間決定
        float excess = Mathf.Max(0, currentTotal - maxCapacity);
        totalOverloadDebt += excess * Time.deltaTime * 0.1f;

        // 超時或主動結束
        if (overloadTimer >= currentOverloadLimit)
        {
            ApplyBacklash();
        }
    }

    private void ApplyBacklash()
    {
        isOverloaded = false;

        // 【新增】賦予極長的強制冷卻時間（例如 10 秒），期間絕對無法再開
        overloadCooldown = 10.0f;

        // 扣除屬性 (反噬)
        magicStats.power /= 1.5f; // 恢復正常

        // 【新增】給予立即性的「痛覺」（扣除實際血量或給予負面獎勵）
        // 這會讓 AI 神經網路立刻收到「亂開爆發會受傷」的強烈負面訊號
        GetComponent<HealthSystem>().TakeDamage(totalOverloadDebt * 5f);
        EcoAgent agent = GetComponent<EcoAgent>();
        if (agent != null) agent.AddReward(-1.0f); // 輕微的懲罰分數

        magicStats.precision -= totalOverloadDebt * 0.05f; // 永久受損

        // 強制回到最大儲存量
        float currentTotal = GetTotalStored();
        if (currentTotal > 0.01f) // 安全檢查
        {
            float ratio = Mathf.Min(1.0f, maxCapacity / currentTotal);
            for (int i = 0; i < 5; i++) storedElements[i] *= ratio;
        }
        else
        {
            // 如果原本就是 0，直接清空即可，不要計算比值
            for (int i = 0; i < 5; i++) storedElements[i] = 0f;
        }
        magicStats.precision = Mathf.Max(0.1f, magicStats.precision - totalOverloadDebt * 0.05f);
        totalOverloadDebt = 0;
    }

    public float GetTotalStored()
    {
        float sum = 0;
        foreach (float f in storedElements) sum += f;
        return sum;
    }

    public void TakeDamage(float damage)
    {

    }
}