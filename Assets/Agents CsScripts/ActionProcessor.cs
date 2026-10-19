using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;
using static Grpc.Core.ChannelOption;

public class ActionProcessor : MonoBehaviour
{
    private Rigidbody rb;
    private AgentMagic magic;
    private InternalFurnace furnace;
    private EvolutionSystem evolution;
    private InternalEnergySystem energySystem;
    private AgentVisualizer visualizer;
    private EcoAgent agent;

    [Header("運動參數")]
    public float moveSpeed = 5f;
    public float turnSpeed = 180f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        magic = GetComponent<AgentMagic>();
        furnace = GetComponent<InternalFurnace>();
        evolution = GetComponent<EvolutionSystem>();
        energySystem = GetComponent<InternalEnergySystem>();
        visualizer = GetComponent<AgentVisualizer>();
        agent = GetComponent<EcoAgent>();
    }

    // 當 ML-Agents 傳回動作時觸發
    public void ExecuteActions(ActionBuffers actions)
    {
        var continuousActions = actions.ContinuousActions;

        // --- 1. 基礎物理運動 ---
        float moveForward = continuousActions[0];
        float moveRight = continuousActions[1];
        float turn = continuousActions[2];

        ApplyMovement(moveForward, moveRight, turn);

        // --- 2. 四肢魔法行為處理 ---
        AgentMagic.ActionParameter[] limbParameters = new AgentMagic.ActionParameter[4];

        for (int i = 0; i < 4; i++)
        {
            int offset = 3 + (i * 10);

            limbParameters[i] = new AgentMagic.ActionParameter
            {
                isActive = continuousActions[offset] > 0,
                // 動作類型映射：-1~-0.5:Release, -0.5~0:Siphon, 0~0.5:Inject, 0.5~1:Push
                targetAction = MapToTargetAction(continuousActions[offset + 1]),
                // 元素類型映射：將 -1~1 映射為 0~4 (ElementType)
                elementType = (GridManager.ElementType)Mathf.Clamp(Mathf.FloorToInt((continuousActions[offset + 2] + 1f) * 2.5f), 0, 4),
                amount = (continuousActions[offset + 3] + 1f) * 0.5f, // 0~1
                radius = (continuousActions[offset + 4] + 1f) * 2.5f, // 0~5 Cells
                // 目標座標：將相對偏移轉化為世界座標
                targetPos = CalculateTargetWorldPos(continuousActions[offset + 5], continuousActions[offset + 6])
            };

            // 如果是 Push 動作，需要額外處理方向向量
            if (limbParameters[i].targetAction == AgentMagic.ActionTarget.Push)
            {
                limbParameters[i].forceDir = new Vector3(
                    continuousActions[offset + 7],
                    continuousActions[offset + 8],
                    continuousActions[offset + 9]
                ).normalized;
            }
        }

        magic.ProcessLimbActions(limbParameters);

        // --- 3. 體內能量運轉與爆發邏輯 (Index: 43~48) ---
        ProcessInternalOperations(continuousActions);

        // --- 3. 熔爐與升級邏輯 ---
        if (continuousActions[43] > 0.8f)
        {
            // 如果滿足條件則嘗試升級
            // evolution.TryStartEvolution(); 
        }

        bool anyActive = false;
        for (int i = 0; i < 4; i++)
        {
            int offset = 3 + (i * 10);
            float isActive = continuousActions[offset];

            if (isActive > 0)
            {
                anyActive = true;
                AgentMagic.ActionTarget target = MapToTargetAction(continuousActions[offset + 1]);
                // 修正：從 actions[offset + 2] 映射到 0-4 的五行索引
                int elementType = Mathf.Clamp(Mathf.FloorToInt((continuousActions[offset + 2] + 1f) * 2.5f), 0, 4);
                float intensity = continuousActions[offset + 3];

                visualizer.PlayMagicEffect(target, elementType, intensity);
            }
        }

        if (!anyActive) visualizer.StopMagicEffects();
    }

    private void ApplyMovement(float forward, float right, float turn)
    {
        // 位移
        Vector3 moveVec = (transform.forward * forward + transform.right * right) * moveSpeed;
        rb.linearVelocity = new Vector3(moveVec.x, rb.linearVelocity.y, moveVec.z);

        // 旋轉
        Quaternion rotation = Quaternion.Euler(0, turn * turnSpeed * Time.fixedDeltaTime, 0);
        rb.MoveRotation(rb.rotation * rotation);
    }

    private void ProcessInternalOperations(ActionSegment<float> actions)
    {
        // [43] 爆發觸發 (Overload Trigger)
        // 如果數值大於 0.5，代表 AI 決定進入爆發狀態
        if (actions[43] > 0.5f && !energySystem.isOverloaded)
        {
            // [44] 決定爆發犧牲量 (將 -1~1 映射為 1~5 點的反噬代價)
            float sacrifice = Mathf.Lerp(1f, 5f, (actions[44] + 1f) * 0.5f);
            energySystem.TriggerOverload(sacrifice);
        }

        // [45] 內部能量轉移速率 (大於 0 代表有意願轉移體內儲存的能量)
        float transferRate = actions[45];
        if (transferRate > 0)
        {
            // [46] 選擇要轉移哪一種元素 (-1~1 映射到 0~4)
            int transElementType = Mathf.Clamp(Mathf.FloorToInt((actions[46] + 1f) * 2.5f), 0, 4);

            // 實際轉移量 (受限於 transferRate 與時間，假設每秒最多轉移 10 點)
            float transAmount = transferRate * 50f * Time.deltaTime;

            // [47] 決定轉移目的地 (-1~-0.33: 熔爐, -0.33~0.33: 升級池, 0.33~1.0: 四肢強化)
            float destination = actions[47];

            if (destination < -0.33f)
            {
                // 目的地：熔爐 (進行相生相剋反應)
                // 註: InternalFurnace 的 InjectElement 內建了 ConsumeEnergy 的扣除檢查
                furnace.InjectElement((GridManager.ElementType)transElementType, transAmount);
            }
            else if (destination < 0.33f)
            {
                // 目的地：進化升級池
                // 必須先從丹田(儲存區)扣除成功，才能注入升級池
                if (energySystem.ConsumeEnergy(transElementType, transAmount, "升級"))
                {
                    evolution.InjectForUpgrade(transElementType, transAmount);
                }

                // 【關鍵：引路麵包屑】
                // 每次成功把能量存入升級池，就給予極微小的正向回饋
                // 假設 transAmount 一幀是 0.1，獎勵就是 0.001。累積起來會讓 AI 非常喜歡這個動作！
                if (agent != null)
                {
                    agent.AddReward(transAmount * 0.01f);
                }
            }
            else
            {
                // 目的地：四肢部位強化
                // [48] 決定要強化哪個部位 (-1~1 映射到 0~3)
                int targetLimb = Mathf.Clamp(Mathf.FloorToInt((actions[48] + 1f) * 2f), 0, 3);

                if (energySystem.ConsumeEnergy(transElementType, transAmount, "部位強化"))
                {
                    energySystem.partReinforcement[targetLimb] += transAmount;
                }
            }
        }
    }

    private AgentMagic.ActionTarget MapToTargetAction(float value)
    {
        if (value < -0.5f) return AgentMagic.ActionTarget.Release;
        if (value < 0f) return AgentMagic.ActionTarget.Siphon;
        if (value < 0.5f) return AgentMagic.ActionTarget.Inject;
        return AgentMagic.ActionTarget.Push;
    }

    private Vector3 CalculateTargetWorldPos(float xOffset, float zOffset)
    {
        // 將 AI 輸出的 -1~1 映射到感知範圍內
        float range = magic.maxPushDistance;
        Vector3 localOffset = new Vector3(xOffset * range, 0, zOffset * range);
        return transform.TransformPoint(localOffset);
    }
}