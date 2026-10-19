using UnityEngine;

public class GridMapVisualizer : MonoBehaviour
{
    [Header("預製體與更新")]
    public GameObject cellPrefab;    // 必須拖入一個預設的 Unity Cube
    public float updateInterval = 0.2f;

    [Header("高度與對齊設定")]
    public float agentCenterY = 1f;      // 生命體中心高度 (Y座標)
    public float agentHeight = 2f;       // 生命體的高度
    public float maxElementValue = 100f; // 當元素總量達到多少時，長方體會長到最高

    private GridManager gridManager;
    private MeshRenderer[,] renderers;
    private Transform[,] cellTransforms; // 緩存 Transform 以提升修改效能
    private Color[] elementColors;

    private float baseY;         // 底面對齊的 Y 座標
    private float maxBarHeight;  // 長方體的最大高度限制

    void Start()
    {
        // 取得顏色配置 (透明度全部改為 1f，即完全不透明)
        elementColors = new Color[5] {
            new Color(1f, 0.8f, 0f, 1f),   // 金
            new Color(0.2f, 1f, 0.2f, 1f), // 木
            new Color(0.2f, 0.5f, 1f, 1f), // 水
            new Color(1f, 0.2f, 0.1f, 1f), // 火
            new Color(0.5f, 0.3f, 0.1f, 1f)  // 土
        };

        // 計算底部基準線與最大高度
        baseY = agentCenterY - (2f * agentHeight);
        maxBarHeight = 2f * agentHeight; // 頂部為 agentCenterY，所以總高為 2 * agentHeight

        InitializeGrid();
        InvokeRepeating(nameof(UpdateGridVisuals), 0.5f, updateInterval);
    }

    void InitializeGrid()
    {
        gridManager = GridManager.Instance;
        int width = gridManager.xSize;
        int height = gridManager.zSize;
        float spacing = gridManager.cellSize;

        renderers = new MeshRenderer[width, height];
        cellTransforms = new Transform[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // 初始生成時先放在底部
                Vector3 pos = new Vector3(x * spacing, baseY, z * spacing);
                GameObject cell = Instantiate(cellPrefab, pos, Quaternion.identity, transform);

                cellTransforms[x, z] = cell.transform;
                // XZ 縮放保持 0.9 倍留縫隙，Y 軸初始設為極小值
                cellTransforms[x, z].localScale = new Vector3(spacing * 0.9f, 0.1f, spacing * 0.9f);

                renderers[x, z] = cell.GetComponent<MeshRenderer>();
                renderers[x, z].enabled = false;
            }
        }
    }

    void UpdateGridVisuals()
    {
        float spacing = gridManager.cellSize;
        float xzScale = spacing * 0.9f;

        for (int x = 0; x < gridManager.xSize; x++)
        {
            for (int z = 0; z < gridManager.zSize; z++)
            {
                var cellData = gridManager.GetElementAt(x, 0, z);

                int dominantType = 0;
                float maxVal = 0;
                float totalVal = 0;

                for (int i = 0; i < 5; i++)
                {
                    totalVal += cellData.elements[i];
                    if (cellData.elements[i] > maxVal)
                    {
                        maxVal = cellData.elements[i];
                        dominantType = i;
                    }
                }

                if (totalVal > 0.1f)
                {
                    renderers[x, z].enabled = true;

                    // 1. 設定顏色 (不透明)
                    renderers[x, z].material.color = elementColors[dominantType];

                    // 2. 計算目標高度 (將元素量映射到 0~1 的比例，並乘上最大高度)
                    float heightRatio = Mathf.Clamp01(totalVal / maxElementValue);
                    float currentHeight = heightRatio * maxBarHeight;

                    // 避免高度變成 0 導致視覺錯誤，給予一個極小的最低高度
                    currentHeight = Mathf.Max(0.05f, currentHeight);

                    // 3. 更新 Transform (動態改變高度與中心點座標)
                    cellTransforms[x, z].localScale = new Vector3(xzScale, currentHeight, xzScale);

                    // Cube 的中心點要在底部的基礎上加上「一半的高度」，底面才會精準對齊 baseY
                    float newY = baseY + (currentHeight / 2f);
                    cellTransforms[x, z].position = new Vector3(x * spacing, newY, z * spacing);
                }
                else
                {
                    // 元素量太低時隱藏
                    renderers[x, z].enabled = false;
                }
            }
        }
    }
}