using UnityEngine;

public class AgentVisualizer : MonoBehaviour
{
    [Header("基礎粒子系統")]
    public ParticleSystem damageParticles;  // 受傷（紅）
    public ParticleSystem gatherParticles;  // 吸收/吸納
    public ParticleSystem releaseParticles; // 釋放/注入
    public ParticleSystem pushParticles;    // 推動特效
    public ParticleSystem evolveParticles;  // 進化特效

    [Header("狀態指示")]
    public MeshRenderer bodyRenderer;       // 用於顯示過熱紅光
    public Transform lingliRing;            // 腳下的靈力環

    [Header("五行顏色")]
    public Color[] elementColors = new Color[5] {
        new Color(1f, 0.8f, 0f),    // 金 (亮黃)
        new Color(0.2f, 1f, 0.2f),  // 木 (翠綠)
        new Color(0.2f, 0.5f, 1f),  // 水 (湛藍)
        new Color(1f, 0.2f, 0.1f),  // 火 (火紅)
        new Color(0.5f, 0.3f, 0.1f) // 土 (深褐)
    };

    private Material bodyMaterial;
    private Color originalBodyColor;

    void Awake()
    {
        if (bodyRenderer != null)
        {
            bodyMaterial = bodyRenderer.material;
            originalBodyColor = bodyMaterial.color;
        }
    }

    // 1. 顯示受傷
    public void PlayDamage()
    {
        if (damageParticles != null && !damageParticles.isPlaying)
            damageParticles.Play();
    }

    // 2. 顯示魔法行為 (吸納/注入/釋放)
    public void PlayMagicEffect(AgentMagic.ActionTarget target, int elementType, float intensity)
    {
        ParticleSystem targetPS = null;

        switch (target)
        {
            case AgentMagic.ActionTarget.Siphon: targetPS = gatherParticles; break;
            case AgentMagic.ActionTarget.Inject:
            case AgentMagic.ActionTarget.Release: targetPS = releaseParticles; break;
            case AgentMagic.ActionTarget.Push: targetPS = pushParticles; break;
        }

        if (targetPS != null)
        {
            var main = targetPS.main;
            main.startColor = elementColors[elementType];

            // 根據強度調整發射量
            var emission = targetPS.emission;
            emission.rateOverTime = intensity * 50f;

            if (!targetPS.isPlaying) targetPS.Play();
        }
    }

    public void StopMagicEffects()
    {
        if (gatherParticles) gatherParticles.Stop();
        if (releaseParticles) releaseParticles.Stop();
        if (pushParticles) pushParticles.Stop();
    }

    // 3. 實時更新狀態 (過熱、靈力環)
    public void UpdateStatus(float lingliRatio, float heatRatio, bool isEvolving)
    {
        // 靈力環縮放
        if (lingliRing != null)
        {
            lingliRing.localScale = new Vector3(lingliRatio, 0.01f, lingliRatio);
        }

        // 過熱紅光 (HeatRatio 0~1)
        if (bodyMaterial != null)
        {
            bodyMaterial.color = Color.Lerp(originalBodyColor, Color.red, heatRatio);
            if (heatRatio > 0.8f) bodyMaterial.EnableKeyword("_EMISSION");
            else bodyMaterial.DisableKeyword("_EMISSION");
        }

        // 進化特效
        if (isEvolving && evolveParticles != null && !evolveParticles.isPlaying)
            evolveParticles.Play();
        else if (!isEvolving && evolveParticles != null)
            evolveParticles.Stop();
    }
}