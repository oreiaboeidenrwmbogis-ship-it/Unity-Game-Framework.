using System.Collections.Generic;
using Template.Audio;
using Template.Core.Logging;
using Template.Core.Services;
using Template.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Template.Demo
{
    /// <summary>
    /// 演示面板集合（纯代码构建，零美术资产）：阶段 2.1 的面板栈演示 + 阶段 2.4 的设置界面。
    ///
    /// ⚠ 为什么要挤在一个文件里（不是偷懒）：
    /// Template.Demo 是 **Editor-only 程序集**（asmdef includePlatforms: Editor，Demo 不进正式包）。
    /// 对于"文件名与类名相同"的 MonoBehaviour，Unity 会把它绑定到脚本资产并做编辑器脚本检查，
    /// 于是运行期 AddComponent 会报 "Can't add script behaviour 'X' because it is an editor script"；
    /// 而放在**文件名与任何类名都不匹配**的文件里（本文件），则走反射路径，运行期可正常添加。
    /// 新增演示面板类请继续放在本文件或其它非同名文件里。
    /// </summary>
    public static class DemoPanelFactory
    {
        /// <summary>
        /// 构建一个演示面板 prefab（未激活）。title/subtitle 渲染为居中文本；
        /// subtitle 传 null 则省略。返回的面板须经 UIService.Open 打开。
        /// </summary>
        public static TPanel Build<TPanel>(string title, string subtitle, Color background, Vector2 size)
            where TPanel : UiPanel
        {
            var go = new GameObject("Panel-" + typeof(TPanel).Name);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;

            Image bg = go.AddComponent<Image>();
            bg.color = background;
            bg.raycastTarget = true;

            Text titleText = CreateText(go.transform, title, 30, new Color(1f, 1f, 1f));
            titleText.rectTransform.anchoredPosition = new Vector2(0f, size.y * 0.5f - 34f);

            if (subtitle != null)
            {
                Text subText = CreateText(go.transform, subtitle, 18, new Color(0.75f, 0.82f, 0.9f));
                subText.rectTransform.anchoredPosition = new Vector2(0f, -12f);
            }

            return go.AddComponent<TPanel>();
        }

        private static Text CreateText(Transform parent, string content, int fontSize, Color color)
        {
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(parent, false);

            Text text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); // 引擎内置字体，演示零资产依赖
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false; // 文本不参与点击拾取，事件穿透到面板/遮罩

            RectTransform rt = text.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(460f, 40f);
            return text;
        }
    }

    /// <summary>全屏页（栈底）：演示“被遮挡隐藏 / 回到栈顶恢复”的生命周期钩子。</summary>
    public sealed class DemoMainMenuScreen : UiPanel
    {
        protected override void OnHidden()
            => Log.Verbose("Demo", "[UI] 主菜单被上层页遮挡 → OnHidden（IsOpen 仍为 true）");

        protected override void OnShown()
            => Log.Verbose("Demo", "[UI] 主菜单回到栈顶 → OnShown（恢复可见）");
    }

    /// <summary>全屏页（压栈）：验证新 Screen 打开时自动隐藏旧页。</summary>
    public sealed class DemoSettingsScreen : UiPanel { }

    /// <summary>弹窗：验证 Popup 层遮罩亮起/释放。</summary>
    public sealed class DemoConfirmPopup : UiPanel
    {
        public override UILayer Layer => UILayer.Popup;
    }

    /// <summary>悬浮提示：验证 Overlay 层渲染在 Popup 之上且不参与返回。</summary>
    public sealed class DemoToastView : UiPanel
    {
        public override UILayer Layer => UILayer.Overlay;
    }

    /// <summary>
    /// 阶段 2.4 设置界面（演示用，运行期代码构建，零资产）。
    ///
    /// 内容：四通道音量滑条。
    /// 关键演示点：**面板只调各系统的公开 API**（AudioService.SetVolume），
    /// 不认识设置系统内部结构 —— 这就是"设置系统是各系统的统一出口"的用法：
    /// 设置项的定义、默认值、持久化都在各模块自己手里，界面只负责读写。
    /// </summary>
    public sealed class DemoSettingsPanel : UiPanel
    {
        private const float PanelWidth = 940f;
        private const float PanelHeight = 460f;

        private static readonly Color TextColor = new Color(0.88f, 0.91f, 0.95f);
        private static readonly Color HintColor = new Color(0.60f, 0.68f, 0.78f);
        private static readonly Color TrackColor = new Color(0.20f, 0.23f, 0.28f);
        private static readonly Color FillColor = new Color(0.35f, 0.62f, 0.85f);

        private AudioService _audio;

        private readonly List<AudioBus> _buses = new List<AudioBus>
        {
            AudioBus.Master, AudioBus.Bgm, AudioBus.Sfx, AudioBus.Voice,
        };
        private readonly List<Slider> _sliders = new List<Slider>();
        private readonly List<Text> _volumeLabels = new List<Text>();

        private Text _status;

        /// <summary>构建面板模板（模板本体不入栈，由 UIService.Open 实例化；演示结束时销毁）。</summary>
        public static DemoSettingsPanel Create()
        {
            var go = new GameObject(nameof(DemoSettingsPanel));
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
            rt.anchoredPosition = Vector2.zero;

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.12f, 0.16f);
            bg.raycastTarget = true;

            return go.AddComponent<DemoSettingsPanel>();
        }

        protected override void OnOpened()
        {
            // 服务经 ServiceLocator 获取（面板由 UIService 实例化，此刻服务早已就绪）
            _audio = ServiceLocator.Get<AudioService>();

            BuildContent();
            RefreshVolumes();
            SetStatus("拖动滑条 → 即改即生效并写入设置文件");
        }

        // ── 构建 ──

        private void BuildContent()
        {
            MakeText(transform, "设置", 28, TextColor, new Vector2(0f, 190f), new Vector2(600f, 40f), TextAnchor.MiddleCenter);
            MakeText(transform, "音量（改动即写入设置文件，下次运行自动读回）", 18, HintColor,
                new Vector2(0f, 145f), new Vector2(880f, 26f), TextAnchor.MiddleLeft);

            for (int i = 0; i < _buses.Count; i++)
            {
                float y = 100f - i * 44f;
                AudioBus bus = _buses[i];

                MakeText(transform, BusLabel(bus), 20, TextColor,
                    new Vector2(-330f, y), new Vector2(150f, 30f), TextAnchor.MiddleLeft);

                Slider slider = MakeSlider(transform, new Vector2(30f, y), new Vector2(400f, 36f));
                int index = i; // 闭包捕获：固定为当前索引
                slider.onValueChanged.AddListener(v => OnVolumeChanged(index, v));
                _sliders.Add(slider);

                _volumeLabels.Add(MakeText(transform, "-", 20, HintColor,
                    new Vector2(350f, y), new Vector2(100f, 30f), TextAnchor.MiddleRight));
            }

            _status = MakeText(transform, string.Empty, 19, HintColor,
                new Vector2(0f, -110f), new Vector2(880f, 30f), TextAnchor.MiddleCenter);

            MakeText(transform, "按 Esc 返回", 17, HintColor,
                new Vector2(0f, -165f), new Vector2(880f, 26f), TextAnchor.MiddleCenter);
        }

        private static string BusLabel(AudioBus bus)
        {
            switch (bus)
            {
                case AudioBus.Master: return "Master 总音量";
                case AudioBus.Bgm: return "BGM 背景乐";
                case AudioBus.Sfx: return "SFX 音效";
                default: return "Voice 语音";
            }
        }

        // ── 交互 ──

        private void OnVolumeChanged(int busIndex, float value)
        {
            AudioBus bus = _buses[busIndex];
            _audio.SetVolume(bus, value); // 服务内部：应用 → 写设置 → 落盘+广播
            UpdateVolumeLabel(busIndex, value);
            SetStatus("已写入设置：" + BusLabel(bus) + " " + Mathf.RoundToInt(value * 100f) + "%");
        }

        // ── 刷新 ──

        private void RefreshVolumes()
        {
            for (int i = 0; i < _buses.Count; i++)
            {
                float value = _audio.GetVolume(_buses[i]);
                _sliders[i].SetValueWithoutNotify(value); // 程序赋值不触发回调（否则会回写一遍设置）
                UpdateVolumeLabel(i, value);
            }
        }

        private void UpdateVolumeLabel(int busIndex, float value)
        {
            _volumeLabels[busIndex].text = Mathf.RoundToInt(value * 100f) + "%";
        }

        private void SetStatus(string message) => _status.text = message;

        // ── uGUI 搭建小工具（演示面板专用，正式项目用预制体 + 美术资产） ──

        private static Text MakeText(Transform parent, string content, int fontSize, Color color,
            Vector2 position, Vector2 size, TextAnchor align)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            Text text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            RectTransform rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return text;
        }

        /// <summary>标准 uGUI 滑条层级（Background + Fill Area/Fill + Handle Slide Area/Handle）——必须齐全，否则不可交互。</summary>
        private static Slider MakeSlider(Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Slider");
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            Slider slider = go.AddComponent<Slider>();

            MakeImage(go.transform, "Background", TrackColor,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -5f), new Vector2(0f, 5f));

            RectTransform fillArea = MakeRect(go.transform, "Fill Area",
                Vector2.zero, Vector2.one, new Vector2(10f, 13f), new Vector2(-10f, -13f));
            Image fill = MakeImage(fillArea, "Fill", FillColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            RectTransform handleArea = MakeRect(go.transform, "Handle Slide Area",
                Vector2.zero, Vector2.one, new Vector2(10f, 11f), new Vector2(-10f, -11f));
            Image handle = MakeImage(handleArea, "Handle", new Color(0.92f, 0.94f, 0.97f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            handle.rectTransform.sizeDelta = new Vector2(20f, 0f); // 宽度固定，高度随区域

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            return slider;
        }

        private static RectTransform MakeRect(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        private static Image MakeImage(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = MakeRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }
    }
}
