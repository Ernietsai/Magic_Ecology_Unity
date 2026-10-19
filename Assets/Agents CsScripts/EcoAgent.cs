using NUnit.Framework.Internal;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

//mlagents-learn config/EcoTrain.yaml --run-id=SectWar_Test_05 --resume --timeout-wait 120
//mlagents-learn config/EcoTrain.yaml --run-id=SectWar_Test_05 --force --timeout-wait 120

//tensorboard --logdir results

public class EcoAgent : Agent
{
    private ActionProcessor actionProcessor;
    private AgentPerception perception;
    private HealthSystem health;
    private InternalEnergySystem energy;
    private AgentMagic magic;
    private InternalFurnace furnace;
    private GridManager gridManager;

    private float lastHP;

    public override void Initialize()
    {
        // 取得所有組件引用
        actionProcessor = GetComponent<ActionProcessor>();
        perception = GetComponent<AgentPerception>();
        health = GetComponent<HealthSystem>();
        energy = GetComponent<InternalEnergySystem>();
        magic = GetComponent<AgentMagic>();
        furnace = GetComponent<InternalFurnace>();
        //gridManager = GridManager.Instance;
        //Debug.Log("gridManager已設定：" + (gridManager != null));
        
        lastHP = health.maxHp;
        gridManager = GridManager.Instance;
    }

    private void Start()
    {

    }

    public override void OnEpisodeBegin()
    {
        // 1. 重置位置 (避開障礙物)
        transform.localPosition = new Vector3(Random.Range(-25, 25), 0f, Random.Range(-25, 25)) + new Vector3(gridManager.xSize * 0.5f, 0f, gridManager.zSize * 0.5f);
        transform.rotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
        GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
        GetComponent<Rigidbody>().angularVelocity = Vector3.zero;

        // 2. 重置狀態
        energy.storedElements[1] = 10f;
        health.hp = health.maxHp;
        lastHP = health.maxHp;
        energy.storedElements = new float[5]; // 清空丹田
        magic.currentLingli = magic.maxLingli;
        furnace.currentHeat = 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 確保 GridManager 已經生成好格點了再進行感應
        // 假設你的 GridManager 有個 bool initialized
        if (perception == null || gridManager == null)
        {
            if (perception == null && gridManager == null)
            {
                Debug.Log("GridManager與AgentPerception為空");
            }
            else if (gridManager == null)
            { 
                Debug.Log("GridManager為空");
            }
            else if (perception == null)
            {
                Debug.Log("AgentPerception為空");
            }
            else
            {
                Debug.Log("GridManager或AgentPerception為空，但都不為空？？？");
            }
            return;
        }

        // 1. 自身狀態 (約 23)
        sensor.AddObservation(perception.GetSelfData());

        // 2. 環境感知 (8射線 * 3段 * 8屬性 = 192)  應該是  (8射線 * 3段 * 13屬性 = 312)
        sensor.AddObservation(perception.GetEnvironmentData());

        // 3. 實體感知 (3目標 * 9屬性 = 27)  應該是  (3目標 * 10屬性 = 30)
        sensor.AddObservation(perception.GetEntityData(3));

        // 4. 靈力狀態 (1)
        sensor.AddObservation(magic.currentLingli / magic.maxLingli);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. 執行動作
        actionProcessor.ExecuteActions(actions);

        // 2. 計算獎勵
        ApplyRewards();

        if (health.hp < lastHP)
        {
            AddReward(-(lastHP - health.hp) * 0.1f);
            lastHP = health.hp;
        }

        // 能量收集獎勵 (測試 AI 是否會主動去魔脈吸能)
        float totalStored = 0;
        foreach (var e in energy.storedElements) totalStored += e;
        if (totalStored > 0) AddReward(totalStored * 0.0001f);

    }

    private void ApplyRewards()
    {
        // --- A. 生存與傷害獎勵 ---
        AddReward(0.001f); // 基礎生存獎勵

        if (health.hp < lastHP)
        {
            float dmg = lastHP - health.hp;
            AddReward(-dmg * 0.1f); // 受傷懲罰
            lastHP = health.hp;
        }

        if (health.hp <= 0)
        {
            SetReward(-1f);
            EndEpisode();
        }

        // --- B. 資源獲取獎勵 ---
        // 檢查最近一次 Siphon 的量 (這部分可由 AgentMagic 提供反饋)
        AddReward(magic.lastSiphonedAmount * 0.05f);

        // --- C. 靈力管理懲罰 ---
        if (magic.currentLingli < 5f)
        {
            AddReward(-0.001f); // 靈力過低微量懲罰，鼓勵節約
        }

        // --- D. 熔爐熱量監控 ---
        float heatRatio = GetComponent<InternalFurnace>().currentHeat / 100f;
        if (heatRatio > 0.8f)
        {
            AddReward(-0.005f * heatRatio); // 懲罰過熱風險
        }
    }

    // 當攻擊到別人時，由對方的 HealthSystem 透過此接口回報獎勵
    public void RegisterAttackSuccess(float damageDealt)
    {
        AddReward(damageDealt * 0.5f);
    }

    // 手動測試模式 (Heuristic)：按下按鍵模擬 AI 輸出
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;

        // 獲取當前鍵盤狀態
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 1. 模擬前後移動 (W/S)
        float forward = 0;
        if (keyboard.wKey.isPressed) forward = 1f;
        else if (keyboard.sKey.isPressed) forward = -1f;
        continuousActions[0] = forward;

        // 2. 模擬旋轉 (A/D)
        float turn = 0;
        if (keyboard.dKey.isPressed) turn = 1f;
        else if (keyboard.aKey.isPressed) turn = -1f;
        continuousActions[2] = turn;

        // 3. 模擬肢體 0 (左手) 釋放火元素 (Space 鍵)
        if (keyboard.spaceKey.isPressed)
        {
            continuousActions[3] = 1f;       // isActive
            continuousActions[4] = -1f;      // Release 模式 (依據你的 Mapping)
            continuousActions[5] = 0.2f;     // 火元素映射值
            continuousActions[6] = 0.5f;     // 強度
            continuousActions[7] = 1.0f;     // 半徑
        }
        else
        {
            continuousActions[3] = -1f;      // 不激活
        }
    }

    public void ApplyEndEpisode(float reward=-10f)
    {
        SetReward(reward);
        EndEpisode();
    }
}

/*
 [Managers]
  - SimulationManager (控制演化代數)
  - GridManager (五行格點運算)
[Environment]
  - Terrain
  - ManaSources (動態生成)
  - Obstacles (五行屬性障礙物)
[Agents]
  - Agent_001 (Prefab)
  - Agent_002
[Pickups]
  - SecretManuals (秘笈 Prefab)
 */