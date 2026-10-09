using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 物理精确抓钩枪控制器
/// 负责状态流转、输入响应、物理力计算及收绳逻辑
/// </summary>
public class GrapplingGun : MonoBehaviour
{
    /// <summary>
    /// 抓钩阶段变更事件，供 LineRenderer、音效、动画等外部系统订阅
    /// </summary>
    public event Action<GrapplePhase> GrapplePhaseChanged; 
    
    /// <summary>当前抓钩锚点的世界坐标</summary>
    public Vector3 GrapplePoint { get; private set; }

    /// <summary>当前所处的抓钩阶段</summary>
    public GrapplePhase CurrentGrapplePhase { get; private set; }

    /// <summary>抓钩生命周期状态枚举</summary>
    public enum GrapplePhase
    {
        Waiting,    // 待机：等待玩家输入
        Launching,  // 发射：弹道飞行中，尚未命中
        Grappling,  // 抓取：已命中锚点，物理模拟进行中
        Retracting  // 回收：未命中或主动释放后，发射器归位动画
    }

    [Header("Reel Settings")]
    [SerializeField] private float _reelInAcceleration;   // 收绳加速度 (m/s²)
    [SerializeField] private float _retractionTime;       // 发射器归位插值时长 (s)
    [SerializeField] private Transform _launcherTransform; // 发射器视觉模型 Transform

    [Header("Player References")]
    [SerializeField] private PlayerMovement _playerMovement; // 玩家移动脚本引用
    [SerializeField] private Rigidbody _playerRigidbody;     // 玩家刚体引用

    // ===== 内部运行时状态 =====
    private bool _isApplyingGrappleForces; // 是否正在施加抓钩物理力
    private float _ropeLength;             // 当前有效绳长（随收绳递减）
    private bool _isRopeInTension;         // 绳索是否处于绷紧状态
    private float _reelInSpeed;            // 当前收绳线速度
    private bool _isReelingIn;             // 是否正在执行收绳
    private Vector3 _launcherOffset;       // 发射器相对于父级的初始本地偏移
    private float _reactionTimer;        // 归位动画计时器

    /// <summary>
    /// 由外部弹道/射线检测组件调用，通知发射结果
    /// </summary>
    /// <param name="isLaunchSuccessful">是否成功命中有效表面</param>
    /// <param name="grapplePoint">命中点世界坐标</param>
    public void LaunchFinished(bool isLaunchSuccessful, Vector3 grapplePoint)
    {
        // 仅在发射阶段接受回调，防止状态污染
        if (CurrentGrapplePhase != GrapplePhase.Launching) return;
        
        GrapplePoint = grapplePoint;

        if (isLaunchSuccessful)
        {
            StartGrapple();
        }
        else
        {
            // 未命中则直接进入回收阶段
            CurrentGrapplePhase = GrapplePhase.Retracting;
            GrapplePhaseChanged?.Invoke(GrapplePhase.Launching);
        }
    }
    
    private void Start()
    {
        CurrentGrapplePhase = GrapplePhase.Waiting;
        // 记录发射器初始本地位置，用于回收时精确归位
        _launcherOffset = _launcherTransform.localPosition;
    }

    private void Update()
    {
        // 【输入：发射】仅在待机状态下响应左键按下
        if (Mouse.current.leftButton.wasPressedThisFrame && CurrentGrapplePhase == GrapplePhase.Waiting)
        {
            CurrentGrapplePhase = GrapplePhase.Launching;
            GrapplePhaseChanged?.Invoke(GrapplePhase.Waiting);
        }

        // 【抓取阶段：持续按住维持，松开释放】
        if (CurrentGrapplePhase == GrapplePhase.Grappling)
        {
            if (Mouse.current.leftButton.isPressed)
            {
                _isApplyingGrappleForces = true;

                // 判断绳索是否绷紧：当前距离 > 绳长时为松弛，否则为绷紧
                // 使用 sqrMagnitude 避免开方运算
                _isRopeInTension = _ropeLength * _ropeLength 
                    < (GrapplePoint - _playerRigidbody.position).sqrMagnitude;
            }
            else
            {
                // 松开按键：停止施力，恢复玩家移动，进入回收
                _isApplyingGrappleForces = false;
                _playerMovement.enabled = true;
                
                CurrentGrapplePhase = GrapplePhase.Retracting;
                GrapplePhaseChanged?.Invoke(GrapplePhase.Grappling);
            }
        }

        // 【回收阶段：发射器视觉归位动画】
        if (CurrentGrapplePhase == GrapplePhase.Retracting)
        {
            if (_reactionTimer < _retractionTime)
            {
                _reactionTimer += Time.deltaTime;
                
                float t = _reactionTimer / _retractionTime;
                // 目标位置 = 玩家当前位置 + 初始本地偏移（跟随玩家）
                Vector3 finalPosition = _playerRigidbody.position + _launcherOffset;

                // 从锚点平滑插值回玩家身上的挂载点
                _launcherTransform.position = Vector3.Lerp(GrapplePoint, finalPosition, t);
            }
            else
            {
                ResetGrapplingGun();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!_isApplyingGrappleForces) return;

        // 每物理帧施加抓钩力
        ApplyGrappleForces();

        // 【防穿透约束】当玩家朝向锚点的径向速度 ≤ 0（即远离或静止）且绳索绷紧时
        // 强制将玩家拉回绳长边界并移除径向速度分量
        if (Vector3.Dot(_playerRigidbody.linearVelocity, GrapplePoint - _playerRigidbody.position) <= 0 
            && _isRopeInTension)
        {
            TugPlayer();
        }

        // 【收绳逻辑】绳长 > 最小阈值时持续加速收回
        if (_ropeLength > 1.5f && _isReelingIn)
        {
            _reelInSpeed += _reelInAcceleration * Time.fixedDeltaTime;
            _ropeLength -= _reelInSpeed * Time.fixedDeltaTime;
        }
        else
        {
            // 达到最小绳长后停止收绳
            _isReelingIn = false;
            _ropeLength = 1.5f;
        }
    }

    /// <summary>
    /// 初始化抓取状态：禁用玩家移动、记录初始绳长、启动收绳
    /// </summary>
    private void StartGrapple()
    {
        _playerMovement.enabled = false;
        _ropeLength = (GrapplePoint - _playerRigidbody.position).magnitude;
        _isReelingIn = true;
        _reelInSpeed = 0;
        
        CurrentGrapplePhase = GrapplePhase.Grappling;
        GrapplePhaseChanged?.Invoke(GrapplePhase.Launching);
    }

    /// <summary>
    /// 核心物理计算：基于圆周运动向心力公式 + 重力径向分量计算绳索张力
    /// T = m(v²/r + g·cosθ)，其中 θ 为绳索与竖直向下方向的夹角
    /// </summary>
    private void ApplyGrappleForces()
    {
        Vector3 direction = (GrapplePoint - _playerRigidbody.position).normalized;
        
        // 计算绳索方向与重力反方向(-down=up)的夹角弧度
        // 注意：此处用 -direction 与 Vector3.down 求角，等价于 direction 与 Vector3.up 的夹角
        float theta = Vector3.Angle(-direction, Vector3.down) * Mathf.Deg2Rad;

        // 向心加速度 a_c = v² / r
        float centripetalAcceleration = _playerRigidbody.linearVelocity.sqrMagnitude / _ropeLength;
        
        // 绳索张力 T = m × (a_c + g·cosθ)
        // cosθ 项补偿了重力在绳索方向上的分量，使摆荡底部张力最大、顶部最小
        Vector3 tension = _playerRigidbody.mass *
                          (centripetalAcceleration + Physics.gravity.magnitude * Mathf.Cos(theta)) 
                          * direction;

        if (_isRopeInTension)
        {
            // 收绳时额外施加沿绳方向的拉力，使玩家向锚点加速
            if (_isReelingIn)
            {
                _playerRigidbody.AddForce(_playerRigidbody.mass * _reelInAcceleration * direction);
            }
            
            // 施加计算得到的张力
            _playerRigidbody.AddForce(tension, ForceMode.Force);
        }
    }

    /// <summary>
    /// 刚性约束：将玩家位置钳制到绳长球面上，并移除径向速度
    /// 防止因数值积分误差导致玩家漂移到绳长之外
    /// </summary>
    private void TugPlayer()
    {
        Vector3 direction = (GrapplePoint - _playerRigidbody.position).normalized;
        
        // 将速度投影到垂直于绳索的切平面上，移除所有径向分量
        // 保留切向速度使摆荡动量不丢失
        Vector3 tangentialVelocity = Vector3.ProjectOnPlane(_playerRigidbody.linearVelocity, direction);
        _playerRigidbody.linearVelocity = tangentialVelocity;
        
        _isRopeInTension = true;
        
        // 强制将玩家位置修正到绳长边界上
        _playerRigidbody.position = GrapplePoint - direction * _ropeLength;
    }

    /// <summary>
    /// 完全重置抓钩枪到待机状态
    /// </summary>
    private void ResetGrapplingGun()
    {
        // 恢复发射器的父子关系和本地变换
        _launcherTransform.parent = transform;
        _launcherTransform.localPosition = _launcherOffset;
        _launcherTransform.localRotation = Quaternion.Euler(0, 0, 90f);
        
        _reactionTimer = 0;
        CurrentGrapplePhase = GrapplePhase.Waiting;
        GrapplePhaseChanged?.Invoke(GrapplePhase.Retracting);
    }
}