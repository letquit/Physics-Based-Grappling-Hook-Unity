using System;
using UnityEngine;

/// <summary>
/// 抓钩音效管理器
/// 响应 GrapplingGun 的阶段变更事件，驱动 AudioSource 播放对应音效
/// 包含命中音效到收绳循环音的自动衔接逻辑
/// </summary>
public class GrappleSoundManager : MonoBehaviour
{
    [Header("Audio Clips")]
    [SerializeField] private AudioClip _launchSound;   // 发射音效（短促）
    [SerializeField] private AudioClip _stickSound;    // 命中/钉入音效（短促）
    [SerializeField] private AudioClip _reelInSound;   // 收绳机械音效（可循环）
    
    // ===== 组件引用 =====
    private GrapplingGun _grapplingGun;  // 同物体上的抓钩主控器
    private AudioSource _audioSource;    // 同物体上的音频源

    /// <summary>
    /// 标记是否正在等待命中音效自然播放完毕
    /// 为 true 时 Update 会持续检测播放状态，结束后自动切入收绳音
    /// </summary>
    private bool _isWaitingForStickSoundToEnd;

    private void Start()
    {
        _grapplingGun = GetComponent<GrapplingGun>();
        _audioSource = GetComponent<AudioSource>();
        
        // 订阅阶段变更事件，建立音频与状态机的绑定
        _grapplingGun.GrapplePhaseChanged += OnGrapplePhaseChanged;
    }

    /// <summary>
    /// 阶段变更回调：根据前后阶段组合决定播放哪个音效
    /// 使用 previousPhase + CurrentPhase 双条件判断，避免同一阶段内重复触发
    /// </summary>
    private void OnGrapplePhaseChanged(GrapplingGun.GrapplePhase previousPhase)
    {
        // Waiting → Launching：播放发射音
        if (_grapplingGun.CurrentGrapplePhase == GrapplingGun.GrapplePhase.Launching &&
            previousPhase == GrapplingGun.GrapplePhase.Waiting)
        {
            PlayLaunchSound();
        }
        // Launching → Grappling：命中成功，播放钉入音并启动衔接等待
        else if (_grapplingGun.CurrentGrapplePhase == GrapplingGun.GrapplePhase.Grappling &&
                 previousPhase == GrapplingGun.GrapplePhase.Launching)
        {
            PlayStickSound();
        }
        // Launching → Retracting：未命中直接回收，跳过钉入音直接播放收绳音
        else if (_grapplingGun.CurrentGrapplePhase == GrapplingGun.GrapplePhase.Retracting &&
                 previousPhase == GrapplingGun.GrapplePhase.Launching)
        {
            PlayReelInSound();
        }
        // 任意阶段 → Waiting：重置完成，立即停止所有音频
        else if (_grapplingGun.CurrentGrapplePhase == GrapplingGun.GrapplePhase.Waiting)
        {
            _audioSource.Stop();
            _isWaitingForStickSoundToEnd = false; // 同步清除衔接标记
        }
    }

    /// <summary>
    /// 播放发射音效：非循环，打断当前播放
    /// </summary>
    private void PlayLaunchSound()
    {
        _audioSource.Stop();

        _audioSource.clip = _launchSound;
        _audioSource.loop = false;
        _audioSource.Play();
    }
    
    /// <summary>
    /// 播放命中音效：非循环，并标记等待其自然结束
    /// 结束后由 Update 自动衔接收绳循环音，保证"叮→嗡—"的听觉连续性
    /// </summary>
    private void PlayStickSound()
    {
        _audioSource.Stop();

        _audioSource.clip = _stickSound;
        _audioSource.loop = false;
        _audioSource.Play();
        
        _isWaitingForStickSoundToEnd = true;
    }
    
    /// <summary>
    /// 播放收绳音效：循环模式，模拟持续的机械卷扬声
    /// </summary>
    private void PlayReelInSound()
    {
        _audioSource.Stop();

        _audioSource.clip = _reelInSound;
        _audioSource.loop = true;
        _audioSource.Play();
    }

    /// <summary>
    /// 每帧检测命中音效是否播完，完成后自动切入收绳循环音
    /// 仅在 _isWaitingForStickSoundToEnd 为 true 时执行检测，零额外开销
    /// </summary>
    private void Update()
    {
        if (_isWaitingForStickSoundToEnd && !_audioSource.isPlaying)
        {
            PlayReelInSound();
            _isWaitingForStickSoundToEnd = false;
        }
    }

    /// <summary>
    /// 销毁时取消事件订阅，防止 GrapplingGun 存活时触发空引用
    /// </summary>
    private void OnDestroy()
    {
        if (_grapplingGun != null)
        {
            _grapplingGun.GrapplePhaseChanged -= OnGrapplePhaseChanged;
        }
    }
}