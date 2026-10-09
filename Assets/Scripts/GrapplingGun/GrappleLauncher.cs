using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 抓钩弹道发射器
/// 挂载在抓钩枪的子物体上，负责物理投射、碰撞检测与发射结果回调
/// 通过订阅 GrapplingGun.GrapplePhaseChanged 事件自动触发发射，实现与主控器的松耦合
/// </summary>
public class GrappleLauncher : MonoBehaviour
{
    [Header("Launch Settings")]
    [SerializeField] private float _launchSpeed;      // 发射初速度 (m/s)
    [SerializeField] private float _maxLaunchTime;    // 最大飞行时间 (s)，超时视为未命中

    // ===== 运行时状态 =====
    private bool _isLaunched;       // 是否正处于飞行中
    private float _launchTimer;     // 剩余飞行时间倒计时

    // ===== 组件引用 =====
    private SphereCollider _sphereCollider;  // 用于碰撞检测的球形碰撞体
    private Rigidbody _rigidbody;            // 物理刚体，飞行时非Kinematic，闲置时Kinematic
    private GrapplingGun _grapplingGun;      // 父级抓钩枪主控器引用

    private void Start()
    {
        _sphereCollider = GetComponent<SphereCollider>();
        _rigidbody = GetComponent<Rigidbody>();
        // 从父级层级中查找 GrapplingGun，建立通信链路
        _grapplingGun = GetComponentInParent<GrapplingGun>();
        
        // 订阅阶段变更事件，当状态从 Waiting → Launching 时自动触发射击
        _grapplingGun.GrapplePhaseChanged += OnGrapplePhaseChanged;
    }

    /// <summary>
    /// 阶段变更回调：仅在 Waiting → Launching 转换时执行发射
    /// 其他阶段转换（如 Grappling → Retracting）不触发任何操作
    /// </summary>
    private void OnGrapplePhaseChanged(GrapplingGun.GrapplePhase previousGrapplePhase)
    {
        if (_grapplingGun.CurrentGrapplePhase == GrapplingGun.GrapplePhase.Launching &&
            previousGrapplePhase == GrapplingGun.GrapplePhase.Waiting)
        {
            Launch();
        }
    }

    /// <summary>
    /// 执行发射：激活物理模拟，施加脉冲力，脱离父级变换
    /// </summary>
    private void Launch()
    {
        _isLaunched = true;
        _launchTimer = _maxLaunchTime;

        // 启用碰撞体和物理模拟
        _sphereCollider.enabled = true;
        _rigidbody.isKinematic = false;
        
        // 使用 Impulse 模式瞬间施加动量：FΔt = m × v
        // 方向取自摄像机前向，确保瞄准即发射
        _rigidbody.AddForce(
            _rigidbody.mass * _launchSpeed * Camera.main.transform.forward, 
            ForceMode.Impulse);

        // ⚠️ 关键：脱离父级，使发射器在世界空间中独立运动
        // 若不解除父子关系，父级（玩家）的移动会叠加到发射器上导致轨迹异常
        transform.parent = null;
    }

    /// <summary>
    /// 结束发射：停用物理模拟，将结果回调给主控制器
    /// </summary>
    /// <param name="isLaunchSuccessful">true=命中有效表面, false=超时/松键/无效碰撞</param>
    private void EndLaunch(bool isLaunchSuccessful)
    {
        _isLaunched = false;

        // 关闭碰撞体和物理模拟，避免闲置时参与物理计算
        _sphereCollider.enabled = false;
        _rigidbody.isKinematic = true;
        
        // 通知主控制器发射结果，由其决定进入 Grappling 还是 Retracting
        _grapplingGun.LaunchFinished(isLaunchSuccessful, transform.position);
    }
    
    private void Update()
    {
        if (!_isLaunched) return;

        // 【超时检测】飞行时间耗尽视为未命中
        _launchTimer -= Time.deltaTime;
        if (_launchTimer < 0)
        {
            EndLaunch(false);
            return;
        }

        // 【输入中断检测】飞行中松开左键视为主动取消
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            EndLaunch(false);
        }
    }

    /// <summary>
    /// 物理碰撞回调：命中表面时吸附到接触点并调整朝向
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        if (!_isLaunched) return;

        // 将发射器精确移动到第一个接触点，消除穿透误差
        transform.position = collision.GetContact(0).point;

        // 调整朝向：使发射器右轴对齐表面法线反方向
        // 用于视觉对齐（如钩爪嵌入墙面的姿态）
        transform.right = -collision.GetContact(0).normal;
            
        EndLaunch(true);
    }

    /// <summary>
    /// 销毁时取消事件订阅，防止空引用异常
    /// </summary>
    private void OnDestroy()
    {
        if (_grapplingGun != null)
        {
            _grapplingGun.GrapplePhaseChanged -= OnGrapplePhaseChanged;
        }
    }
}