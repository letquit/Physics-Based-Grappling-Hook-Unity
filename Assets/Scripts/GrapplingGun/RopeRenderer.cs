using UnityEngine;

/// <summary>
/// 抓钩绳索渲染器
/// 使用 LineRenderer 在枪口与发射器/锚点之间绘制动态连线
/// 仅在非 Waiting 阶段激活，Waiting 时自动隐藏以避免视觉残留
/// </summary>
public class RopeRenderer : MonoBehaviour
{
    // ===== 组件引用 =====
    private GrapplingGun _grapplingGun;   // 父级抓钩主控器，用于读取当前阶段
    private LineRenderer _lineRenderer;   // 本物体上的线段渲染器
    private GrappleLauncher _launcher;    // 发射器引用，作为绳索终点位置源

    private void Start()
    {
        // 从父级获取抓钩主控器
        _grapplingGun = GetComponentInParent<GrapplingGun>();

        // 获取自身挂载的 LineRenderer
        _lineRenderer = GetComponent<LineRenderer>();

        // 从抓钩主控器的子级中查找发射器
        // 发射器在飞行时会脱离父级，但 Transform 引用仍然有效
        _launcher = _grapplingGun.GetComponentInChildren<GrappleLauncher>();
    }

    private void Update()
    {
        // 【非待机阶段】显示绳索并实时更新两端位置
        if (_grapplingGun.CurrentGrapplePhase != GrapplingGun.GrapplePhase.Waiting)
        {
            // 延迟启用：仅在需要时才开启 LineRenderer，避免空闲时的渲染开销
            if (!_lineRenderer.enabled)
            {
                _lineRenderer.enabled = true;
            }

            // 索引0 = 绳索起点（枪口位置，即本渲染器所在Transform）
            _lineRenderer.SetPosition(0, transform.position);

            // 索引1 = 绳索终点（发射器当前位置）
            // Launching 阶段：终点跟随飞行中的发射器
            // Grappling 阶段：发射器已吸附到锚点，终点即为抓钩锚点
            // Retracting 阶段：发射器正在归位插值，终点平滑回到枪口
            _lineRenderer.SetPosition(1, _launcher.transform.position);
        }
        else
        {
            // 【待机阶段】隐藏绳索
            // 使用 enabled 开关而非销毁/重建，避免每帧 GC 和组件初始化开销
            _lineRenderer.enabled = false;
        }
    }
}