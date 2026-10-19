using UnityEngine;

public class HealthSystem : MonoBehaviour
{
    [Header("基礎狀態")]
    public float hp = 100f;
    public float maxHp = 100f;
    public float physicalResistance = 0.1f; // 物理減傷

    private GridManager gridManager;
    private AgentMagic magic;
    private InternalEnergySystem energySystem;
    private InternalFurnace furnace;
    private Rigidbody rb;
    private AgentVisualizer visualizer;
    private EcoAgent ecoAgent;
    private string lastDamageSource = "";

    void Awake()
    {
        magic = GetComponent<AgentMagic>();
        energySystem = GetComponent<InternalEnergySystem>();
        furnace = GetComponent<InternalFurnace>();
        rb = GetComponent<Rigidbody>();
        visualizer = GetComponent<AgentVisualizer>();
        gridManager = GridManager.Instance;
        ecoAgent = GetComponent<EcoAgent>();
    }

    private void Start()
    {

    }

    void Update()
    {
        ApplyGridDamage();

        // 自動回血邏輯 (可選：消耗體內「木」元素來回血)
        if (hp < maxHp) ApplyRegeneration();
    }

    private void ApplyGridDamage()
    {
        // 1. 獲取生命體中心點所在的格點
        var cell = gridManager.GetElementAt(transform.position);

        float totalElementDensity = 0;
        for (int i = 0; i < 5; i++) totalElementDensity += cell.elements[i];

        if (totalElementDensity < 0.05f) return; // 稀薄能量不造成傷害

        // 2. 身分識別 (豁免/加成)
        // 取得格點頻率與自身的匹配度
        float match = gridManager.GetFrequencyMatch(cell.frequency, magic.currentFrequency);

        // 豁免邏輯：
        // Match > 0.9 (完全同源)：100% 豁免傷害 (甚至可以吸收)
        // Match 0.5 ~ 0.9 (同門)：大幅減傷
        // Match < 0 (敵對/異質)：全額傷害，甚至加成
        float protection = (match > 0.9f) ? 0f : Mathf.Clamp01(1f - match);

        // 3. 計算物理衝擊 (Kinetic Damage)
        // 傷害來自：格點速度與自身速度的「相對差」
        Vector3 relativeVel = cell.velocity - rb.linearVelocity;
        float kineticDmg = 0.5f * totalElementDensity * relativeVel.sqrMagnitude * 0.05f;

        // 4. 計算元素侵蝕 (Elemental Damage)
        // 這裡可以實作生剋：如果格點中充滿了剋制你體內主元素的能量，傷害加倍
        float elementalDmg = totalElementDensity * cell.controlValue * 2.0f;

        // 5. 最終傷害施加
        float finalDamage = (kineticDmg + elementalDmg) * protection * Time.deltaTime;

        if (finalDamage > 0.1f)
        {
            TakeDamage(finalDamage, "元素傷害");
        }
    }

    public void TakeDamage(float amount, string source = "")
    {
        hp -= amount;

        // 穩定度衝擊：受到傷害時，熔爐會產生額外熱量 (氣血翻騰)
        furnace.currentHeat += amount * 0.5f;

        if (visualizer != null)
            visualizer.PlayDamage(); // 播放受傷紅光粒子
        if (!gameObject.name.Contains("("))
        {
            //Debug.Log($"{gameObject.name} 受到 {amount:F2} 點傷害，剩餘 HP: {hp:F2}，來源: {source}");
        }
        lastDamageSource = source;

        if (hp <= 0) Die();
    }

    private void ApplyRegeneration()
    {
        // 範例：消耗體內「木」元素來恢復 HP
        float woodEnergy = energySystem.storedElements[(int)GridManager.ElementType.Wood];
        if (woodEnergy > 0.1f)
        {
            float heal = Time.deltaTime * 2.0f;
            hp = Mathf.Min(maxHp, hp + heal);
            energySystem.ConsumeEnergy((int)GridManager.ElementType.Wood, heal * 0.5f);
        }
    }

    private void Die()
    {
        // 死亡時噴發丹田能量回饋給環境
        for (int i = 0; i < 5; i++)
        {
            float releaseAmount = energySystem.storedElements[i] * 0.8f;  //防止元素過量
            gridManager.AddImpulseSphere(transform.position, 3f, 0.5f, Vector3.zero, (GridManager.ElementType)i, releaseAmount);
        }
        if (lastDamageSource == "元素傷害")
        {
            Debug.Log($"{gameObject.name}受到{lastDamageSource}而死");
        }
        //Debug.Log($"{gameObject.name} 已耗盡生機，能量回饋天地。");
        ecoAgent.ApplyEndEpisode();
        //Destroy(gameObject); // 或觸發 ML-Agents 的 EndEpisode
    }
}