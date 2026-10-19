using System.Collections;
using UnityEngine;

public class EvolutionSystem : MonoBehaviour
{
    [Header("升級能量池")]
    public float[] upgradePool = new float[5]; // 儲存五種升級能量
    public int upgradeCount = 0;
    public bool isEvolving = false;

    private float dissipationRate = 0.05f; // 緩慢消散
    private AgentPerception perception;

    public void Awake()
    {
        perception = GetComponent<AgentPerception>();
    }

    public void InjectForUpgrade(int type, float amount)
    {
        if (isEvolving) return;
        // 檢查共享冷卻...
        upgradePool[type] += amount;
    }

    void Update()
    {
        if (!isEvolving)
        {
            // 能量消散邏輯
            for (int i = 0; i < 5; i++)
                upgradePool[i] *= (1f - dissipationRate * Time.deltaTime);

            CheckEvolutionRequirements();
        }
    }

    private void CheckEvolutionRequirements()
    {
        // 計算總能量與天分/次數的關係...
        if (CanEvolve()) StartCoroutine(EvolutionProcess());
    }

    private IEnumerator EvolutionProcess()
    {
        isEvolving = true;
        // 1. 降低風險屬性 (如：神識、速度)
        float originalSense = perception.mentalSense;
        perception.mentalSense *= 0.2f;

        // 2. 等待一段隨機時間 (受天分影響)
        float duration = CalculateDuration();
        yield return new WaitForSeconds(duration);

        // 3. 結算屬性提升
        ApplyAttributeGains();

        // 4. 清空能量池並恢復屬性
        for (int i = 0; i < 5; i++) upgradePool[i] = 0;
        perception.mentalSense = originalSense;
        upgradeCount++;
        isEvolving = false;
    }

    private bool CanEvolve()
    {
        // 1. 設定動態門檻：階級越高，需要的能量呈指數/線性上升
        float baseRequiredEnergy = 100f;
        float requiredEnergy = baseRequiredEnergy + (upgradeCount * 50f);

        // 2. 只要有任何一個單一屬性的升級池滿了，就可以引發突破
        for (int i = 0; i < 5; i++)
        {
            if (upgradePool[i] >= requiredEnergy)
            {
                return true;
            }
        }
        return false;
    }

    private float CalculateDuration()
    {// 突破所需時間：天分 (mentalSense) 越高，閉關時間越短 (最少 2 秒)
        float baseTime = 10.0f;
        return Mathf.Max(2.0f, baseTime / Mathf.Max(1f, perception.mentalSense));
        //return 10f;
    }

    private void ApplyAttributeGains()
    {
        // 1. 找出是哪一種五行元素促成了這次進化 (決定進化方向)
        int dominantType = 0;
        float maxEnergy = 0;
        for (int i = 0; i < 5; i++)
        {
            if (upgradePool[i] > maxEnergy)
            {
                maxEnergy = upgradePool[i];
                dominantType = i;
            }
        }

        // 2. 獲取其他系統的引用
        AgentMagic magic = GetComponent<AgentMagic>();
        HealthSystem health = GetComponent<HealthSystem>();
        InternalEnergySystem energy = GetComponent<InternalEnergySystem>();

        // 3. 【洗髓伐骨】全方位基礎屬性提升
        health.maxHp += 50f;
        health.hp = health.maxHp; // 突破後狀態全滿 (重要機制)

        magic.power += 5f;
        magic.maxLingli += 50f;
        magic.currentLingli = magic.maxLingli;

        energy.maxCapacity += 50f;
        perception.mentalSense += 0.5f; // 神識擴張

        // 4. 【特化分支】根據主屬性給予額外專屬天賦
        switch (dominantType)
        {
            case 0: // 金：物理防禦提升
                health.physicalResistance += 0.1f;
                break;
            case 1: // 木：靈力回復速度加快
                magic.lingliRegen += 2f;
                break;
            case 2: // 水：法術冷卻縮短
                magic.cooldownStat += 0.2f;
                break;
            case 3: // 火：法術破壞力大增
                magic.power += 10f;
                break;
            case 4: // 土：生命上限額外暴增
                health.maxHp += 50f;
                health.hp = health.maxHp;
                break;
        }

        // 5. 【強化學習核心】給予巨額獎勵！
        EcoAgent agent = GetComponent<EcoAgent>();
        if (agent != null)
        {
            // 給予超大 Reward！這會讓 AI 在未來的訓練中，把「存能量升級」視為最高目標
            agent.AddReward(15.0f);
            Debug.Log($"[{gameObject.name}] 突破至第 {upgradeCount + 1} 境界！主修屬性: {(GridManager.ElementType)dominantType}");
        }

        // 6. 播放華麗特效
        AgentVisualizer visualizer = GetComponent<AgentVisualizer>();
        if (visualizer != null && visualizer.evolveParticles != null)
        {
            visualizer.evolveParticles.Play();
        }
    }
}