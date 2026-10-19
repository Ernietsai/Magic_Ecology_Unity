using UnityEngine;
using TMPro; // 引用 TextMeshPro

public class GameUIManager : MonoBehaviour
{
    public Canvas mainCanvas;
    public GameObject bottomPanel;
    public TextMeshProUGUI statsText;

    private bool isUIHidden = false;

    void Update()
    {
        // F1 鍵：切換整體 UI 顯示/隱藏
        if (Input.GetKeyDown(KeyCode.F1))
        {
            isUIHidden = !isUIHidden;
            mainCanvas.enabled = !isUIHidden;
        }
    }

    // 由攝影機呼叫，用於更新當前生命體的數值
    public void UpdateAgentInfo(GameObject agent)
    {
        if (agent == null || !bottomPanel.activeSelf) return;

        HealthSystem hpSys = agent.GetComponent<HealthSystem>();
        AgentMagic magic = agent.GetComponent<AgentMagic>();
        InternalEnergySystem energy = agent.GetComponent<InternalEnergySystem>();
        EvolutionSystem evo = agent.GetComponent<EvolutionSystem>();

        if (hpSys == null || energy == null) return;

        // 格式化排版 (模擬物品欄的多欄位顯示)
        string info = $"【{agent.name}】 境界: {evo.upgradeCount} | HP: {hpSys.hp:F0}/{hpSys.maxHp:F0} | 魔力: {magic.currentLingli:F0}/{magic.maxLingli:F0}\n";
        info += $"金: {energy.storedElements[0]:F1} | 木: {energy.storedElements[1]:F1} | 水: {energy.storedElements[2]:F1} | 火: {energy.storedElements[3]:F1} | 土: {energy.storedElements[4]:F1}";

        statsText.text = info;
    }

    // 切換底部狀態欄 (觀察者模式時隱藏)
    public void SetBottomPanelActive(bool isActive)
    {
        bottomPanel.SetActive(isActive);
    }
}