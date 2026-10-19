using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    private GridManager gridManager;

    [System.Serializable]
    public struct ManaVein
    {
        public string name;
        public Vector3 position;
        public float radius;
        public GridManager.ElementType mainElement;
        public float flowIntensity; // 噴發強度
        public float frequency;     // 原生頻率
    }

    [Header("魔脈設定")]
    public List<ManaVein> veins = new List<ManaVein>();

    [Header("自然背景（雜訊分布）")]
    public bool usePerlinNoise = true;
    public float noiseScale = 0.1f;

    void Awake()
    {
        gridManager = GridManager.Instance;
    }

    private void Start()
    {
        //Debug.Log("EcoAgent 獲取 GridManager 狀態：" + (gridManager != null));
        GenerateMap();
    }

    public void GenerateMap()
    {
        // 1. 初始化基礎地圖地形
        SetupTerrain();

        // 2. 如果使用 Perlin Noise，讓地圖起始就有一點混亂的游離能量
        if (usePerlinNoise) ApplyNaturalNoise();

        // 3. 配置門派據點（魔脈中心）
        SetupManaVeins();
    }

    private void SetupTerrain()
    {
        // 可以在這裡定義邊界牆壁或障礙物
        // 例如：在地圖邊緣設置「岩石」標記 (TerrainType = 1)
    }

    private void ApplyNaturalNoise()
    {
        for (int x = 0; x < gridManager.xSize; x++)
        {
            for (int y = 0; y < gridManager.ySize; y++)
            {
                for (int z = 0; z < gridManager.zSize; z++)
                {
                    float n = Mathf.PerlinNoise(x * noiseScale, z * noiseScale);
                    if (n > 0.7f) // 在雜訊高點注入少量隨機元素
                    {
                        int type = Random.Range(0, 5);
                        gridManager.InjectAtCell(x, 0, z, (GridManager.ElementType)type, 0.5f);
                    }
                }
            }
        }
    }

    private void SetupManaVeins()
    {
        // 這裡通常是手動在 Inspector 設定好的據點
        foreach (var vein in veins)
        {
            Debug.Log($"魔脈生成：{vein.name} 於 {vein.position}");
            // 初始在魔脈位置噴發一次大量能量
            //gridManager.AddImpulseSphere(vein.position, vein.radius, 0.8f, Vector3.zero, vein.mainElement, 50f, vein.frequency, 0.5f);
        }
    }
}