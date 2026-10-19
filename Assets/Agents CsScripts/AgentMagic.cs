using System.Collections;
using UnityEngine;
using static GridManager;
using static UnityEditor.PlayerSettings;

public class AgentMagic : MonoBehaviour
{
    [Header("靈力系統")]
    public float maxLingli = 100f;
    public float currentLingli = 100f;
    public float lingliRegen = 5f;

    [Header("能力屬性")]
    public float power = 10f;
    public float precision = 0.8f;
    public float maxPushDistance = 25f;
    public float cooldownStat = 1.0f;

    [Header("行為偏移")]
    public float releaseOffset = 0.15f;
    public float siphonOffset = 0.15f;

    [Header("狀態")]
    public bool isCoolingDown = false;
    public bool[] limbIsCoolingDown = { false, false, false, false };

    private GridManager gridManager;
    private InternalEnergySystem energySystem;
    public float currentFrequency = 0f; // 應該由門派或初始化設定
    [HideInInspector] public float lastSiphonedAmount = 0f;

    private void Awake()
    {
        gridManager = GridManager.Instance;
    }
    
    void Start()
    {
        energySystem = GetComponent<InternalEnergySystem>();
        currentLingli = maxLingli;
        if (gridManager == null)
        {
            Debug.LogError("找不到場景中的 Grid Manager 物件！請確認腳本已掛載。");
        }
    }
    void Update()
    {
        // 靈力自然恢復
        currentLingli = Mathf.Min(maxLingli, currentLingli + lingliRegen * Time.deltaTime);
    }

    public enum ActionTarget { Release, Siphon, Inject, Push }

    public struct ActionParameter
    {
        public ActionTarget targetAction;
        public Vector3 targetPos; // 用於遠程操作
        public GridManager.ElementType elementType;
        public float amount;
        public float radius;
        public bool isActive;
        public Vector3 forceDir; // 只有 Push 會使用
    }

    // 計算最大施法距離
    public float MaxPushDistance => power * 2.0f;

    // 核心施法指令
    public enum MagicBodyParts { LeftArm = 0, RightArm = 1, LeftLeg = 2, RightLeg = 3 }

    // 施法天分：手臂 > 腿部
    public float[] releaseTalents = new float[4] { 1.0f, 1.0f, 0.8f, 0.8f };
    // 吸收天分：腿部 > 手臂 (例如：腿部更擅長從地面汲取能量)
    public float[] siphonTalents = new float[4] { 0.7f, 0.7f, 1.0f, 1.0f };

    public void ProcessLimbActions(ActionParameter[] limbActions)
    {
        lastSiphonedAmount = 0f; // 重置本次行動的吸收量統計
        if (currentLingli <= 0) return;

        for (int i = 0; i < limbActions.Length; i++)
        {
            if (!limbActions[i].isActive) continue;

            switch (limbActions[i].targetAction)
            {
                case ActionTarget.Release:
                    ExecutePartRelease(i, limbActions[i]);
                    if (!gameObject.name.Contains("("))
                    {
                        //Debug.Log($"執行元素釋放：肢體 {i}，元素 {limbActions[i].elementType}，數量 {limbActions[i].amount}");
                    }
                    break;
                case ActionTarget.Siphon:
                    ExecutePartSiphon(i, limbActions[i]);
                    if (!gameObject.name.Contains("("))
                    {
                        //Debug.Log($"執行元素吸收：肢體 {i}，元素 {limbActions[i].elementType}，數量 {limbActions[i].amount}");
                    }
                    break;
                case ActionTarget.Inject:
                    ExecutePartInject(i, limbActions[i]);
                    if (!gameObject.name.Contains("("))
                    {
                        //Debug.Log($"執行靈力注入：肢體 {i}，目標位置 {limbActions[i].targetPos}，數量 {limbActions[i].amount}");
                    }
                    break;
                case ActionTarget.Push:
                    ExecutePartPush(i, limbActions[i]);
                    if (!gameObject.name.Contains("("))
                    {
                        //Debug.Log($"執行遠程推動：肢體 {i}，目標位置 {limbActions[i].targetPos}，數量 {limbActions[i].amount}");
                    }
                    break;
            }
        }
    }

    // 1. 元素釋放 (體表) - 初始掌控度高
    private void ExecutePartRelease(int limbIdx, ActionParameter p)
    {
        if (!energySystem.ConsumeEnergy((int)p.elementType, p.amount, "元素釋放")) return;

        Vector3 spawnPos = GetLimbSurfacePoint(limbIdx, releaseOffset);
        // 剛釋放的元素：掌控度 1.0, 頻率為自身頻率
        gridManager.AddImpulseSphere(spawnPos, p.radius, 1.0f, Vector3.zero, p.elementType, p.amount, currentFrequency, 1.0f);
        StartLimbCooldown(ActionTarget.Release, limbIdx);
    }

    // 2. 靈力注入 (遠程) - 消耗靈力進行標記
    private void ExecutePartInject(int limbIdx, ActionParameter p)
    {
        float dist = Vector3.Distance(transform.position, p.targetPos);
        float cost = (p.amount * 0.5f) * (1f + dist / maxPushDistance);

        if (currentLingli >= cost)
        {
            currentLingli -= cost;
            gridManager.UpdateControl(p.targetPos, p.radius, currentFrequency, p.amount * precision);
        }
        Vector3 pos = GetLimbSurfacePoint(limbIdx, siphonOffset);
        var cell = gridManager.GetElementAt(pos);
        //Debug.Log($"靈力注入：消耗 {cost:F2} 靈力，剩餘 {currentLingli:F2}，目標位置 {p.targetPos}, 數量 {p.amount}，範圍中 {gridManager.IsInBounds(pos)}，目前控制度 {cell.controlValue}");  //gridManager.GetFrequencyMatch(cell.frequency, currentFrequency, cell.controlValue)
        StartLimbCooldown(ActionTarget.Inject, limbIdx);
    }

    // 3. 遠程推動 (遠程) - 推力大小取決於掌控度
    private void ExecutePartPush(int limbIdx, ActionParameter p)
    {
        // 1. 計算距離與成本
        // 使用 sqrMagnitude 效能較好，但分母建議也使用平方以保持比例歸一化
        float sqrDist = (transform.position - p.targetPos).sqrMagnitude;
        float maxSqrDist = maxPushDistance * maxPushDistance;

        // 距離因子：越遠消耗越高，最多增加到 2 倍消耗 (1f + 1f)
        float distanceFactor = Mathf.Clamp01(sqrDist / maxSqrDist);
        float cost = p.amount * 1.0f * (1f + distanceFactor);

        if (currentLingli >= cost)
        {
            var cell = gridManager.GetElementAt(p.targetPos);
            // 獲取頻率匹配與掌控度的綜合判定
            float match = gridManager.GetFrequencyMatch(cell.frequency, currentFrequency, cell.controlValue);

            // --- 情況 A：掌控權佔優，執行推動 ---
            if (match > 0.5f)
            {
                currentLingli -= cost;

                // 使用 AI 輸出的方向 p.forceDir，若為零則預設為推離方向
                Vector3 pushDirection = p.forceDir != Vector3.zero ? p.forceDir.normalized : (p.targetPos - transform.position).normalized;

                // 最終推力 = 屬性力量 * AI 投入強度 * 掌控度平方衰減
                // 加入 p.amount 讓 AI 可以微調力道
                Vector3 finalForce = pushDirection * power * p.amount * Mathf.Pow(match, 2f);

                gridManager.AddImpulseSphere(p.targetPos, p.radius, precision, finalForce, GridManager.ElementType.None, 0);
            }
            // --- 情況 B：敵方高度掌控，強行干預導致反噬 ---
            else if (match < 0.3f && cell.controlValue > 0.7f)
            {
                // 敵方的掌控度越高，反噬傷害越重
                energySystem.TakeDamage(cell.controlValue * 10f);
                currentLingli -= cost * 2f; // 懲罰性靈力消耗

                // 反噬發生後不進入一般冷卻，直接返回
                return;
            }
        }

        // 無論成功與否都進入肢體冷卻，防止無消耗連發
        StartLimbCooldown(ActionTarget.Push, limbIdx);
    }

    // 4. 元素吸收 (體表) - 掌控度達標才能吸
    private void ExecutePartSiphon(int limbIdx, ActionParameter p)
    {
        Vector3 pos = GetLimbSurfacePoint(limbIdx, siphonOffset);
        var cell = gridManager.GetElementAt(pos);
        float match = 1;//gridManager.GetFrequencyMatch(cell.frequency, currentFrequency);//, cell.controlValue);

        if (match > 0.6f) // 吸收門檻
        {
            float siphoned = gridManager.SiphonElementSphere(pos, p.radius, precision, p.elementType, p.amount);
            energySystem.AddEnergy((int)p.elementType, siphoned, "元素吸收");
            // 吸收回饋靈力
            currentLingli = Mathf.Min(maxLingli, currentLingli + siphoned * 0.2f);
            lastSiphonedAmount += siphoned;
            if (siphoned > 0)
            {
                //Debug.Log($"成功吸收 {siphoned:F2} 單位的元素 {p.elementType}，頻率匹配 {match:F2}");
            }
        }
        else
        {
            //Debug.Log($"吸收失敗：頻率匹配 {match:F2} 未達標 (需 > 0.6)，位置 {pos}");
        }
        StartLimbCooldown(ActionTarget.Siphon, limbIdx);
    }

    // 肢體數據參考 (假設你已經有肢體位置計算)
    private Vector3 GetLimbSurfacePoint(int limbIndex, float offset)
    {
        // 邏輯：體中心 + 肢體方向 * (體型半徑 + 偏移)
        //return transform.position + transform.forward * offset;//limbs[limbIndex].position + limbs[limbIndex].forward * offset;
        float bodyRadius = transform.localScale.x * 0.5f;
        // 根據肢體索引決定方向 (0:左手, 1:右手, 2:左腳, 3:右腳)
        Vector3[] dirs = { -transform.right, transform.right, -transform.right - transform.forward, transform.right - transform.forward };
        return transform.position + dirs[limbIndex].normalized * (bodyRadius + offset);
    }

    private IEnumerator StartCooldown(string actionType)
    {
        isCoolingDown = true;
        // 冷卻時間公式：基礎時間 / 冷卻屬性 (屬性越高冷卻越快)
        float baseCD = actionType == "Release" ? 1.5f : 0.8f;
        yield return new WaitForSeconds(baseCD / cooldownStat);
        isCoolingDown = false;
    }

    private IEnumerable StartLimbCooldown(ActionTarget actionType, int limbIdx)
    {
        limbIsCoolingDown[limbIdx] = true;
        // 冷卻時間公式：基礎時間 / 冷卻屬性 (屬性越高冷卻越快)
        float baseCD;
        switch (actionType)
        {
            case (ActionTarget.Release):
                baseCD = 0.6f;
                break;
            case (ActionTarget.Siphon):
                baseCD = 0.6f;
                break;
            case (ActionTarget.Inject):
                baseCD = 0.6f;
                break;
            case (ActionTarget.Push):
                baseCD = 0.8f;
                break;
            default:
                baseCD = 1.0f;
                Debug.Log("ValueError:冷卻函式動作種類提供錯誤");
                break;
        }
        yield return new WaitForSeconds(baseCD / cooldownStat);
        limbIsCoolingDown[limbIdx] = false;
    }
}