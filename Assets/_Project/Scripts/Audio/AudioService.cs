using System;
using System.Collections.Generic;
using Template.Core.Eventing;
using Template.Core.Logging;
using Template.Core.Services;
using Template.Settings;
using Template.UI;
using UnityEngine;

namespace Template.Audio
{
    /// <summary>
    /// 音频服务：BGM / SFX / Voice 三通道 + Master 总控，独立音量、淡入淡出、SFX 池化。
    ///
    /// 结构（全部运行期代码构建，零资产依赖 —— clone 即用，与 UI 面板同一思路）：
    ///     [AudioService]（DontDestroyOnLoad）
    ///     ├─ Master  ← 总音量系数（乘到每条通道上）
    ///     ├─ BGM     ← 双 AudioSource 交叉淡变（切歌不中断旧曲）
    ///     ├─ SFX     ← AudioSource 池（并发叠加；2D 平铺播放，3D 按世界坐标衰减，播完自动回收）
    ///     └─ Voice   ← 单 AudioSource（新语音打断旧语音）
    ///
    /// 音量：玩家面向的是 0~1 线性值（设置界面滑条）。持久化统一交给设置系统
    /// （<see cref="AudioSettings.VolumeKeys"/>，键 id "audio.{bus}.volume"）：SetVolume 写入设置，
    /// Init 时从设置拉取初值并订阅后续变更。设置系统缺席时自动降级为"仅本次运行生效、不持久化"。
    ///
    /// 分层实现说明：四通道以"每通道增益系数"实现（Source.volume = 淡变量 × 业务音量 × 通道增益），
    /// 未使用 AudioMixer —— 运行期无法用代码创建 Mixer 分组（2022.3 的 AudioMixer 没有 CreateGroup，
    /// 只有 SetFloat/GetFloat 等操作既有资产的方法），而模板铁律是零资产依赖。
    /// 需要效果器分层（混响/压缩/低通）的项目：在编辑器里创建 AudioMixer 资产并挂到本服务的
    /// 通道源上（<see cref="MixerOwner"/> 预留绑定点），届时增益可改由 Mixer 参数施加，本类 API 不变。
    /// <see cref="AudioVolume"/> 提供线性 ↔ dB 换算，供接入 Mixer 时直接使用。
    ///
    /// 淡变：Tick 以 unscaled 时间推进 —— 暂停（timeScale=0）时音频操作仍应生效（暂停菜单淡入、
    /// 暂停压低 BGM 都靠它）。BGM 交叉淡变：新曲起播淡入的同时旧曲淡出，曲间无静音缝。
    ///
    /// 业务面板音效（阶段 2.3 联动示例）：订阅 UI 的 UIPanelOpenedEvent/UIPanelClosedEvent 播放
    /// 默认界面音 —— 音频模块不认识任何具体面板，事件载荷只含面板名与层级。
    ///
    /// 用法：
    ///     _audio.PlayBgm(bgmClip, fadeSeconds: 1.5f);      // 切歌（首播同样走淡入）
    ///     _audio.PlaySound(clickClip);                     // 2D 音效（UI 点击）
    ///     _audio.PlaySoundAt(hitClip, transform.position); // 3D 空间音（枪声/脚步）
    ///     _audio.PlayVoice(lineClip);                      // 语音（打断上一句）
    ///     _audio.SetVolume(AudioBus.Sfx, 0.6f);            // 设置界面滑条
    /// </summary>
    public sealed class AudioService : IGameService, ITickable
    {
        // 运行期构建的音频基础设施
        private GameObject _root;

        // 设置系统（可选依赖：模块缺席时音量仅本次运行有效，见类注释）
        private SettingsService _settings;

        // BGM：双源交叉淡变（_bgmCurrent 正在淡入/播放，_bgmPrevious 正在淡出）
        private AudioSource _bgmSourceA, _bgmSourceB;
        private bool _bgmAIsCurrent = true;
        private VoiceHandle _bgmHandle;   // 当前曲的淡变状态
        private VoiceHandle _bgmFadeOut;  // 旧曲的淡出状态

        private AudioSource _voiceSource;
        private VoiceHandle _voiceHandle;
        private bool _voiceStarted;

        private readonly List<PooledSource> _sfxPool = new List<PooledSource>();
        private int _sfxActiveCount;

        // 四路线性音量（0~1），索引 = AudioBus；初值来自设置系统，缺省全 1
        private readonly float[] _volumes = { 1f, 1f, 1f, 1f };

        // 音量变更日志节流（设置界面拖动滑条会每帧改音量，逐帧打日志会刷屏）
        private const float VolumeLogInterval = 0.35f;
        private float _lastVolumeLogTime = -999f;

        /// <summary>
        /// 通道源挂点（供需要 AudioMixer 效果器分层的项目把自己创建的 Mixer 挂上去）。
        /// 挂上后 AudioSource.outputAudioMixerGroup 由业务设置，音量仍归本服务管（增益系数照常生效）。
        /// </summary>
        public Transform MixerOwner => _root != null ? _root.transform : null;

        /// <summary>读取某通道线性音量（0~1，设置界面滑条用）。</summary>
        public float GetVolume(AudioBus bus) => _volumes[(int)bus];

        /// <summary>当前是否有 BGM 在播放（淡入中或已就绪）。</summary>
        public bool IsBgmPlaying => _bgmHandle.Source != null && _bgmHandle.Source.isPlaying;

        public void Init()
        {
            BuildAudioRoot();

            // 拉取初值 + 订阅后续变更（两者都要：只订阅会漏初值，只拉取则收不到后续改动）
            _settings = ServiceLocator.TryGet<SettingsService>(out SettingsService settings) ? settings : null;
            if (_settings != null)
            {
                for (int i = 0; i < _volumes.Length; i++)
                    _volumes[i] = AudioVolume.Clamp01(_settings.Get(AudioSettings.VolumeKeys[i]));
                EventBus<SettingsChangedEvent<float>>.Subscribe(OnVolumeSettingChanged);
            }

            EventBus<UIPanelOpenedEvent>.Subscribe(OnPanelOpened);
            EventBus<UIPanelClosedEvent>.Subscribe(OnPanelClosed);

            Log.Info("Audio", "音频系统就绪：Master/BGM/SFX/Voice 四通道，界面音联动已接通{0}",
                _settings != null ? "，音量已接入设置系统（改动即存）" : "（设置系统缺席：音量不持久化）");
        }

        public void Dispose()
        {
            if (_settings != null)
                EventBus<SettingsChangedEvent<float>>.Unsubscribe(OnVolumeSettingChanged);
            EventBus<UIPanelOpenedEvent>.Unsubscribe(OnPanelOpened);
            EventBus<UIPanelClosedEvent>.Unsubscribe(OnPanelClosed);

            StopAll(); // 先停播并解绑 clip，再交根给引擎销毁（"边播边拆"是最容易出问题的拆除顺序）
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _sfxPool.Clear();
            _sfxActiveCount = 0;
        }

        /// <summary>
        /// 停止一切播放并解绑 clip 引用（BGM / 语音 / 音效池）。业务在销毁自己传入的 clip 之前，
        /// 或退出 Play 的拆卸阶段调用 —— 音源持有已销毁 clip 会给引擎的音频层留下悬空指针。
        /// </summary>
        public void StopAll()
        {
            StopAndUnbind(_bgmHandle.Source);
            StopAndUnbind(_bgmFadeOut.Source);
            StopAndUnbind(_voiceHandle.Source);
            _bgmHandle = default;
            _bgmFadeOut = default;
            _voiceHandle = default;
            _voiceStarted = false;

            for (int i = 0; i < _sfxPool.Count; i++)
            {
                StopAndUnbind(_sfxPool[i].Source);
                _sfxPool[i].InUse = false;
                _sfxPool[i].StartedPlayback = false;
            }
            _sfxActiveCount = 0;
        }

        /// <summary>停播并解绑单个音源（对象可能已被引擎先行销毁，故判空）。</summary>
        private static void StopAndUnbind(AudioSource source)
        {
            if (source == null)
                return;
            source.Stop();
            source.clip = null;
        }

        // ── 音量 ──

        /// <summary>设置通道线性音量（0~1），实时生效并持久化。</summary>
        public void SetVolume(AudioBus bus, float linear)
        {
            linear = AudioVolume.Clamp01(linear);
            if (Mathf.Approximately(_volumes[(int)bus], linear))
                return;

            _volumes[(int)bus] = linear;
            RefreshSourceVolumes();
            // 写入设置系统 → 落盘 + 广播（设置系统缺席时仅本次运行生效）
            _settings?.Set(AudioSettings.VolumeKeys[(int)bus], linear);

            // 日志节流：拖动滑条时只按间隔打点（最终值由设置系统的"已保存"日志兜底）
            if (Time.unscaledTime - _lastVolumeLogTime < VolumeLogInterval)
                return;
            _lastVolumeLogTime = Time.unscaledTime;
            Log.Info("Audio", "音量 {0} → {1:P0}（{2}）", bus, linear,
                bus == AudioBus.Master ? "总控" : "实时生效");
        }

        /// <summary>设置系统广播的音量变更（设置界面改音量 / 将来存档导入）→ 应用。</summary>
        private void OnVolumeSettingChanged(SettingsChangedEvent<float> e)
        {
            for (int i = 0; i < AudioSettings.VolumeKeys.Length; i++)
            {
                if (e.Key.Equals(AudioSettings.VolumeKeys[i]))
                {
                    SetVolume((AudioBus)i, e.Value);
                    return;
                }
            }
        }

        /// <summary>总音量合并结果（设置界面回显"实际响度"用）。</summary>
        public float GetEffectiveVolume(AudioBus bus)
        {
            return bus == AudioBus.Master
                ? _volumes[(int)AudioBus.Master]
                : _volumes[(int)AudioBus.Master] * _volumes[(int)bus];
        }


        /// <summary>
        /// 音量变更后的同步：所有受管 AudioSource 的音量在此重算 ——
        /// Source.volume = 相对淡变量 × 该轨音量 × 通道增益（Master × 该通道音量）。
        /// 这样淡变进度与玩家设定永不互相覆盖。
        /// </summary>
        private void RefreshSourceVolumes()
        {
            ApplySourceVolume(_bgmHandle, AudioBus.Bgm);
            ApplySourceVolume(_bgmFadeOut, AudioBus.Bgm);
            ApplySourceVolume(_voiceHandle, AudioBus.Voice);
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                if (_sfxPool[i].InUse)
                    ApplySourceVolume(_sfxPool[i].Source, _sfxPool[i].Volume, AudioBus.Sfx);
            }
        }

        /// <summary>合成并写入单个音源的最终音量。</summary>
        private void ApplySourceVolume(VoiceHandle handle, AudioBus bus)
        {
            if (handle.Source != null)
                ApplySourceVolume(handle.Source, handle.Fade * handle.TrackVolume, bus);
        }

        /// <summary>合成并写入单个音源的最终音量（fade = 相对淡变量 × 该轨基础音量）。</summary>
        private void ApplySourceVolume(AudioSource source, float fade, AudioBus bus)
        {
            if (source != null)
                source.volume = Mathf.Clamp01(fade * SourceGainOf(bus));
        }

        /// <summary>通道增益系数 = Master × 该通道音量（与相对淡变量、业务音量相乘后写入 Source.volume）。</summary>
        private float SourceGainOf(AudioBus bus)
        {
            return GetEffectiveVolume(bus);
        }

        // ── 播放：音效 ──

        /// <summary>播放 2D 音效（UI / 无方位提示音）。可并发叠加，播放完自动回收。</summary>
        public AudioSource PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (!ValidateClip(clip, nameof(PlaySound)))
                return null;

            float trackVolume = Mathf.Clamp01(volume);
            RentSfxSource(trackVolume, out AudioSource source);
            source.clip = clip;
            source.transform.SetParent(_root.transform, false); // 2D：无需跟随
            source.transform.localPosition = Vector3.zero;
            source.spatialBlend = 0f;
            source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            source.loop = false;
            ApplySourceVolume(source, trackVolume, AudioBus.Sfx);
            source.Play();
            _sfxActiveCount++;
            return source;
        }

        /// <summary>播放 3D 空间音效（枪声/脚步/爆炸）：按世界坐标衰减，可跟随移动物体。</summary>
        public AudioSource PlaySoundAt(AudioClip clip, Vector3 position, float volume = 1f,
            float pitch = 1f, Transform follow = null, float minDistance = 1f, float maxDistance = 25f)
        {
            if (!ValidateClip(clip, nameof(PlaySoundAt)))
                return null;

            float trackVolume = Mathf.Clamp01(volume);
            RentSfxSource(trackVolume, out AudioSource source);
            source.clip = clip;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = Mathf.Max(0.01f, minDistance);
            source.maxDistance = Mathf.Max(source.minDistance, maxDistance);
            source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
            source.loop = false;
            ApplySourceVolume(source, trackVolume, AudioBus.Sfx);

            if (follow != null)
            {
                source.transform.SetParent(follow, false);
                source.transform.localPosition = Vector3.zero;
            }
            else
            {
                source.transform.SetParent(_root.transform, false);
                source.transform.position = position;
            }
            source.Play();
            _sfxActiveCount++;
            return source;
        }

        /// <summary>停止某音效源（通常无需手动调用：播完自动回收；提前打断时用）。</summary>
        public void StopSound(AudioSource source)
        {
            if (source == null)
                return;
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                if (_sfxPool[i].Source == source && _sfxPool[i].InUse)
                {
                    RecycleAt(i);
                    return;
                }
            }
        }

        // ── 播放：BGM ──

        /// <summary>
        /// 播放/切换背景音乐（单轨）。已有 BGM 时交叉淡变：新曲淡入的同时旧曲淡出。
        /// fadeSeconds = 0 立即切换。
        /// </summary>
        public void PlayBgm(AudioClip clip, float fadeSeconds = 1f, float volume = 1f, bool loop = true)
        {
            if (!ValidateClip(clip, nameof(PlayBgm)))
                return;

            // 上一轮淡出若尚未走完，直接终止（否则该源会继续以残留音量出声）
            if (_bgmFadeOut.Source != null)
            {
                _bgmFadeOut.Source.Stop();
                _bgmFadeOut = default;
            }

            // 旧曲接棒淡出（曲间交叉：新曲淡入与旧曲淡出同时进行，无静音缝）
            AudioSource old = _bgmHandle.Source;
            if (old != null && old.isPlaying)
            {
                _bgmFadeOut = new VoiceHandle
                {
                    Source = old,
                    StartFade = _bgmHandle.Fade, // 从当前淡变进度起淡出（半途切歌不跳音量）
                    Fade = _bgmHandle.Fade,
                    TargetFade = 0f,
                    TrackVolume = _bgmHandle.TrackVolume,
                    FadeDuration = fadeSeconds,
                    Elapsed = 0f,
                    StopOnFinish = true,
                };
            }

            AudioSource next = _bgmAIsCurrent ? _bgmSourceB : _bgmSourceA;
            _bgmAIsCurrent = !_bgmAIsCurrent;
            next.clip = clip;
            next.loop = loop;
            next.pitch = 1f;
            next.time = 0f;

            float target = Mathf.Clamp01(volume);
            float startFade = fadeSeconds > 0f ? 0f : 1f;
            _bgmHandle = new VoiceHandle
            {
                Source = next,
                StartFade = startFade,
                Fade = startFade,
                TargetFade = 1f,
                TrackVolume = target,
                FadeDuration = fadeSeconds,
                Elapsed = 0f,
            };
            ApplySourceVolume(next, _bgmHandle.Fade * _bgmHandle.TrackVolume, AudioBus.Bgm);
            next.Play();
            _bgmSourceA.transform.localPosition = Vector3.zero;
            _bgmSourceB.transform.localPosition = Vector3.zero;

            Log.Info("Audio", "BGM 起播 '{0}'（时长 {1:F1}s，音量 {2:P0}，{3}）", clip.name, clip.length, target,
                fadeSeconds > 0f ? $"淡入 {fadeSeconds:F1}s" : "立即");
        }

        /// <summary>停止 BGM（fadeSeconds = 0 立即停）。</summary>
        public void StopBgm(float fadeSeconds = 1f)
        {
            if (_bgmHandle.Source == null)
                return;
            // ★ 淡出起点 = 当前音量：AdvanceFade 是 Lerp(StartFade, TargetFade, t)，
            //   不更新起点的话 StartFade 还是淡入时的 0 → Lerp(0,0,t) ≡ 0 → 音量瞬间归零（等于硬切）。
            _bgmHandle.StartFade = _bgmHandle.Fade;
            _bgmHandle.TargetFade = 0f;
            _bgmHandle.FadeDuration = fadeSeconds;
            _bgmHandle.Elapsed = 0f;
            _bgmHandle.StopOnFinish = true;
            if (fadeSeconds <= 0f)
                FinishBgmStop();
        }

        private void FinishBgmStop()
        {
            if (_bgmHandle.Source != null)
                _bgmHandle.Source.Stop();
            _bgmHandle = default;
        }

        // ── 播放：语音 ──

        /// <summary>播放语音（单轨：新语音立即打断上一句 —— 对话连续推进的默认语义）。</summary>
        public void PlayVoice(AudioClip clip, float volume = 1f, float fadeSeconds = 0.05f)
        {
            if (!ValidateClip(clip, nameof(PlayVoice)))
                return;

            _voiceSource.Stop();
            _voiceSource.clip = clip;
            _voiceSource.loop = false;
            _voiceSource.pitch = 1f;
            _voiceHandle = new VoiceHandle
            {
                Source = _voiceSource,
                Fade = fadeSeconds > 0f ? 0f : 1f,
                TargetFade = 1f,
                TrackVolume = Mathf.Clamp01(volume),
                FadeDuration = fadeSeconds,
                Elapsed = 0f,
            };
            ApplySourceVolume(_voiceSource, _voiceHandle.Fade * _voiceHandle.TrackVolume, AudioBus.Voice);
            _voiceStarted = false;
            _voiceSource.Play();
        }

        /// <summary>停止语音（fadeSeconds = 0 立即停）。</summary>
        public void StopVoice(float fadeSeconds = 0.05f)
        {
            if (_voiceHandle.Source == null)
                return;
            _voiceHandle.StartFade = _voiceHandle.Fade; // ★ 同 StopBgm：淡出起点必须是当前音量
            _voiceHandle.TargetFade = 0f;
            _voiceHandle.FadeDuration = fadeSeconds;
            _voiceHandle.Elapsed = 0f;
            _voiceHandle.StopOnFinish = true;
            if (fadeSeconds <= 0f)
            {
                _voiceHandle.Source.Stop();
                _voiceHandle = default;
            }
        }

        // ── 每帧：淡变推进 + 音效源回收 ──

        /// <summary>
        /// 以 unscaled 时间推进（暂停中音频操作仍生效），Bootstrap 每帧驱动。
        /// </summary>
        public void Tick()
        {
            float dt = Time.unscaledDeltaTime;
            AdvanceFade(ref _bgmHandle, dt, AudioBus.Bgm, Track.BgmCurrent);
            AdvanceFade(ref _bgmFadeOut, dt, AudioBus.Bgm, Track.BgmFading);
            AdvanceFade(ref _voiceHandle, dt, AudioBus.Voice, Track.Voice);
            ClearFinishedVoice();
            RecycleFinishedSfx();
        }

        /// <summary>播放轨标识 —— 淡出收尾时按它决定要清理哪个句柄（ref 形参本身不携带身份信息）。</summary>
        private enum Track { BgmCurrent, BgmFading, Voice }

        private void AdvanceFade(ref VoiceHandle handle, float dt, AudioBus bus, Track track)
        {
            if (handle.Source == null)
                return;

            handle.Elapsed += dt;
            float t = handle.FadeDuration <= 0f ? 1f : Mathf.Clamp01(handle.Elapsed / handle.FadeDuration);
            // 由固定起点朝目标线性插值（不可就地插值 —— 连续改写起点会退化成指数逼近）
            handle.Fade = Mathf.Lerp(handle.StartFade, handle.TargetFade, t);
            ApplySourceVolume(handle.Source, handle.Fade * handle.TrackVolume, bus);

            if (t < 1f)
                return;

            handle.Fade = handle.TargetFade;
            ApplySourceVolume(handle.Source, handle.Fade * handle.TrackVolume, bus);
            if (!handle.StopOnFinish)
                return;

            // 该轨终结：停源 + 清句柄。ref 形参即字段本身，赋值回去即等价于清空对应播放轨。
            handle.Source.Stop();
            if (track == Track.Voice)
                _voiceStarted = false; // 否则"已播完的语音"占着句柄挡住后续状态判断
            handle = default;
        }

        /// <summary>语音自然播完（无淡出标记）时清理句柄 —— 语义与音效池一致的"播完即终"。</summary>
        private void ClearFinishedVoice()
        {
            if (_voiceHandle.Source == null)
                return;
            if (_voiceSource.isPlaying)
            {
                _voiceStarted = true; // 起播确认（起播当帧 isPlaying 不为 true，不能立即判完）
                return;
            }
            if (_voiceStarted)
            {
                _voiceHandle = default;
                _voiceStarted = false;
            }
        }

        private void RecycleFinishedSfx()
        {
            if (_sfxActiveCount == 0)
                return;
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                PooledSource pooled = _sfxPool[i];
                if (!pooled.InUse)
                    continue;
                if (pooled.Source.isPlaying)
                {
                    pooled.StartedPlayback = true; // 起播确认（跨帧判定，见下）
                    continue;
                }
                // 未在播放且早已起播过 = 播完；刚租出尚未起播（isPlaying 在起播当帧不返回 true）
                // 不能回收，否则会出现"音效没响就被回收"
                if (pooled.StartedPlayback)
                {
                    RecycleAt(i);
                    i--; // 列表被缩短
                }
            }
        }

        // ── 音效源池（自持，不与业务共用 PoolService —— 音频源是服务内部实现细节） ──

        private void RentSfxSource(float volume, out AudioSource source)
        {
            for (int i = 0; i < _sfxPool.Count; i++)
            {
                if (!_sfxPool[i].InUse)
                {
                    _sfxPool[i].InUse = true;
                    _sfxPool[i].StartedPlayback = false;
                    _sfxPool[i].Volume = volume;
                    _sfxPool[i].Source.gameObject.SetActive(true);
                    source = _sfxPool[i].Source;
                    return;
                }
            }

            var go = new GameObject("SfxSource_" + _sfxPool.Count);
            go.transform.SetParent(_root.transform, false);
            AudioSource created = go.AddComponent<AudioSource>();
            ConfigureSource(created);
            _sfxPool.Add(new PooledSource { Source = created, InUse = true, StartedPlayback = false, Volume = volume });
            source = created;
        }

        private void RecycleAt(int index)
        {
            PooledSource pooled = _sfxPool[index];
            pooled.Source.Stop();
            pooled.Source.clip = null;
            pooled.Source.transform.SetParent(_root.transform, false);
            pooled.Source.gameObject.SetActive(false);
            pooled.InUse = false;
            pooled.Volume = 1f;
            _sfxActiveCount = Mathf.Max(0, _sfxActiveCount - 1);
        }

        // ── 构建 ──

        private void BuildAudioRoot()
        {
            _root = new GameObject("[AudioService]");
            UnityEngine.Object.DontDestroyOnLoad(_root);

            _bgmSourceA = CreateChannelSource("BgmSourceA", loop: true);
            _bgmSourceB = CreateChannelSource("BgmSourceB", loop: true);
            _voiceSource = CreateChannelSource("VoiceSource", loop: false);
        }

        private AudioSource CreateChannelSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            ConfigureSource(source);
            source.loop = loop;
            return source;
        }

        private static void ConfigureSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            // 需要混音效果器的项目：在这里挂 outputAudioMixerGroup（见 MixerOwner 注释）
        }

        private static bool ValidateClip(AudioClip clip, string api)
        {
            if (clip != null)
                return true;
            Log.Warn("Audio", "{0}: clip 为空，已忽略", api);
            return false;
        }

        // ── UI 事件联动（模块间只经事件通信：音频不认识任何具体面板） ──

        private void OnPanelOpened(UIPanelOpenedEvent e)
        {
            // 弹窗用更"重"的音，全屏页用轻音 —— 仅示例默认；真实项目在 Boot 阶段整体替换
            AudioClip clip = e.Layer == UILayer.Popup ? AudioFeedback.DefaultPopupOpen : AudioFeedback.DefaultScreenOpen;
            if (clip != null)
                PlaySound(clip, volume: 1f);
        }

        private void OnPanelClosed(UIPanelClosedEvent e)
        {
            if (AudioFeedback.DefaultClose != null)
                PlaySound(AudioFeedback.DefaultClose, volume: 0.8f);
        }

        // ── 内部类型 ──

        /// <summary>池中的音效源。（class 而非 struct：池列表按引用改字段，避免拆装箱歧义）</summary>
        private sealed class PooledSource
        {
            public AudioSource Source;
            public bool InUse;
            public bool StartedPlayback; // 本次租用是否已确认起播（区分"刚租出"与"已播完"）
            public float Volume;         // 该次播放的业务音量（0~1，音量变更后重算 Source.volume 用）
        }

        /// <summary>
        /// 单条播放轨的运行时状态。音量语义：Source.volume = Fade × TrackVolume × 通道增益 ——
        /// Fade 是 0~1 的相对淡变量（淡变只改它），TrackVolume 是这次播放的业务音量。
        /// </summary>
        private struct VoiceHandle
        {
            public AudioSource Source;
            public float StartFade;   // 淡变起点（0~1 相对量）
            public float Fade;        // 当前相对淡变量（每帧由 StartFade→TargetFade 线性插值）
            public float TargetFade;
            public float TrackVolume; // 该次播放的基础音量
            public float FadeDuration;
            public float Elapsed;
            public bool StopOnFinish;
        }
    }
}
