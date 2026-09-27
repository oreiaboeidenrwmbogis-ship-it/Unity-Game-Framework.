using System;
using System.Collections.Generic;
using Template.Save; // ISaveSection / ISaveSerializer / SaveSlotInfo / SaveData（G 只替省了"取服务"那一类 using）
using UnityEngine;

namespace Jam
{
    // ═══════════════════════════════════════════════════════════════════════
    // 存档系统的用法：三步
    //   ① 写一个"状态类"（你自己的数据，普通 C# 类）
    //   ② 写一个"分片"（把状态类接到存档系统：存 = 序列化成一段文本，读 = 反序列化搬回来）
    //   ③ 在 Boot/场景早期 RegisterSection，之后随时 Save / Load
    // 存档系统本身不认识你的数据 —— 它只管槽位、文件、版本、损坏降级。
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ① 你的存档数据。就是普通 C# 类，但有两条 JsonUtility 的硬性要求：
    /// <c>[Serializable]</c> + **公开字段**（属性不存、私有字段要 [SerializeField]）；
    /// 不支持 Dictionary / 接口 / 多态（要的话得换 ISaveSerializer 实现）。
    /// 字段初始值 = 新建档时的默认值。
    /// </summary>
    [Serializable]
    public sealed class PlayerState
    {
        public int Level = 1;
        public int Gold = 100;
        public float Hp = 100f;
        public Vector3 Position;
        public List<string> Flags = new List<string>();
    }

    /// <summary>
    /// ② 分片：存档系统与你的数据之间唯一的桥。它只要求你交出"一段文本"，格式完全由你定
    /// （这里直接复用存档系统的 serializer，也可以自己拼字符串/换别的格式）。
    /// </summary>
    public sealed class PlayerSection : ISaveSection
    {
        private readonly PlayerState _state;

        public PlayerSection(PlayerState state) => _state = state;

        /// <summary>唯一键，约定 <c>"模块.用途"</c>。重复注册会被拒绝：每段数据只能有一个所有者。</summary>
        public string Key => "player.state";

        /// <summary>存：把当前状态变成文本。返回 null/空串也允许，读的时候会当"缺这段"处理。</summary>
        public string Capture(ISaveSerializer serializer) => serializer.Serialize(_state);

        /// <summary>
        /// 读：把文本搬回你的状态对象。返回 <c>false</c> = "这段读不了"（版本不符/内容异常）——
        /// 存档系统记一条日志就跳过，**其它分片照常恢复**，不会拖垮整个读档。
        /// </summary>
        public bool Restore(string payload, ISaveSerializer serializer)
        {
            try
            {
                PlayerState loaded = serializer.Deserialize<PlayerState>(payload);

                // 逐字段搬进"同一个对象"（别写 _state = loaded：外部还持有旧引用）
                _state.Level = loaded.Level;
                _state.Gold = loaded.Gold;
                _state.Hp = loaded.Hp;
                _state.Position = loaded.Position;
                // ⚠ 老档可能没有这个字段 → JsonUtility 会给 null，所以必须自己兜底
                _state.Flags = loaded.Flags ?? new List<string>();
                return true;
            }
            catch (Exception)
            {
                return false; // 保持当前（默认）状态
            }
        }
    }

    /// <summary>
    /// ③ 使用：挂到场景里任意物体上。想试就按 Play → F1 → 敲 <c>save</c> / <c>load</c>。
    /// （想用快捷键试：在 Update 里加 <c>if (Keyboard.current.f5Key.wasPressedThisFrame) Save();</c>）
    /// </summary>
    public sealed class SaveExample : MonoBehaviour
    {
        [Tooltip("槽位号：任意 ≥ 0 的整数（对应文件 saves/slot_N.json），不需要预先创建")]
        public int Slot;

        private readonly PlayerState _state = new PlayerState();

        private void Awake()
        {
            // ★ 注册分片：必须在任何 Load 之前（Boot / 场景早期）。
            //   老存档里没有这个分片是正常的（该功能当时还不存在）→ 走默认值 + 一条 Warn 日志。
            G.Save.RegisterSection(new PlayerSection(_state));

            // 顺手给 F1 面板加一节（可删）
            G.PanelSection("存档示例", () =>
                $"槽位 {Slot}：关卡 {_state.Level} · 金币 {_state.Gold} · 血量 {_state.Hp:F0}");

            // 顺手加两条作弊命令（可删）：F1 面板输入框里敲 save / load
            G.Cheat("save", "save [槽位] —— 存档（默认当前槽位）", args =>
            {
                if (args.Length > 0 && int.TryParse(args[0], out int parsed)) Slot = parsed;
                Save();
                return "已存档到槽位 " + Slot;
            });
            G.Cheat("load", "load [槽位] —— 读档", args =>
            {
                if (args.Length > 0 && int.TryParse(args[0], out int parsed)) Slot = parsed;
                return Load() ? "读档完成（看 Console）" : "读档失败（看 Console 红字）";
            });

            G.Info("Save", "存档目录：{0}", G.Save.StorageDescription); // 就是 persistentDataPath/saves
        }

        // ── 存 ──

        /// <summary>存档。<paramref name="summary"/>（摘要）会显示在存档列表里，业务自己拼。</summary>
        public void Save()
        {
            string summary = $"第 {_state.Level} 关 · 金币 {_state.Gold}";

            bool reported = G.Save.Save(Slot, summary);
            // ⚠ 注意：Save 现在**恒返回 true** —— 写盘失败（磁盘满 / 只读 / 被占用）只在 Console 记 Error。
            //    所以"存上了没有"要自己确认一次（能读回才算真存上）：
            bool reallyStored = G.Save.TryGetSlotInfo(Slot, out SaveSlotInfo info);

            if (reported && reallyStored)
                G.Info("Save", "已存档到槽位 {0}（{1}）", Slot, summary);
            else
                G.Warn("Save", "存档可能没落盘：reported={0}，能读回={1}", reported, reallyStored);

            // 顺便演示 TryGetSlotInfo 的产物（存档列表界面就是用它拼一行的）
            G.Verbose("Save", "槽位概览：{0}", info);
        }

        // ── 读 ──

        /// <summary>读档。<c>error</c> 有两种含义：加载失败的原因，或"成功但降级了"的原因。</summary>
        public bool Load()
        {
            bool loaded = G.Save.Load(Slot, out string error);

            if (!loaded)
            {
                // 完全没读到（没有档 / 主档和备份都坏了）→ 业务决策：开新档继续，别白屏
                G.Warn("Save", "槽位 {0} 没有可用的档：{1}（按新档继续）", Slot, error);
                return false;
            }

            if (error != null)
            {
                // ⚠ loaded == true 但 error 非空 = **降级成功**：主档坏了，用了上一次保存的备份档。
                //    这里适合提示玩家"最近一次保存丢了"，而不是当成失败。
                G.Warn("Save", "读档降级：{0}", error);
            }

            G.Info("Save", "读档完成：关卡 {0}，金币 {1}，血量 {2:F0}，标记 [{3}]",
                _state.Level, _state.Gold, _state.Hp, string.Join("、", _state.Flags));
            return true;
        }

        /// <summary>只想看看存档里有什么、不想动游戏状态时用它（TryReadData 不恢复任何分片）。</summary>
        public void Peek()
        {
            if (!G.Save.TryReadData(Slot, out SaveData data, out string error))
            {
                G.Warn("Save", "读不出来：{0}", error);
                return;
            }
            G.Info("Save", "存档 v{0}，{1} 个分片，摘要「{2}」，写入时版本 {3}",
                data.Version, data.Sections.Count, data.Meta.Summary, data.Meta.AppVersion);

            foreach (SaveSectionEntry entry in data.Sections)
                G.Verbose("Save", "  分片 {0}：{1} 字符", entry.Key, entry.Payload == null ? 0 : entry.Payload.Length);
        }

        // ── 槽位管理（存档界面用）──

        /// <summary>列出所有已有存档的槽位，并逐个打印概览（存档列表界面就是这段）。</summary>
        public void ListAll()
        {
            int[] slots = G.Save.ListSlots();
            G.Info("Save", "共 {0} 个存档：", slots.Length);
            foreach (int s in slots)
            {
                if (G.Save.TryGetSlotInfo(s, out SaveSlotInfo info))
                    G.Info("Save", "  {0}", info); // ToString() 已经排好版：槽位 0：v1 · 第 1 关 · 2026-09-26 10:30
            }
        }

        public void Delete()
        {
            bool deleted = G.Save.Delete(Slot);
            G.Info("Save", "删除槽位 {0}：{1}", Slot, deleted ? "已删除（主档 + 备份）" : "本来就没有");
        }

        // ───────────────────────────────────────────────────────────────────
        // 版本迁移：**改存档结构时**才需要（改字段名 / 拆分片 / 换格式）。
        // 只加字段不用管（老档缺字段 → 你在 Restore 里兜底即可）。
        //
        // 三步：
        //   ① 把 SaveService.CurrentVersion 改成 2（那是基座里的一行常量，改它等于宣布"格式变了"）
        //   ② 在 Boot 阶段注册下面这条迁移
        //   ③ 迁移只做"结构搬运"，不做业务判断（纯函数才好测）
        //
        //     private sealed class V1ToV2 : ISaveMigration
        //     {
        //         public int FromVersion => 1;                       // 本步把 v1 改造成 v2
        //         public void Apply(SaveData data, ISaveSerializer serializer)
        //         {
        //             SaveSectionEntry entry = data.FindSection("player.state");
        //             if (entry == null) return;                     // 老档没这段 → 什么都不用搬
        //             var old = serializer.Deserialize<PlayerStateV1>(entry.Payload);
        //             var now = new PlayerState { Level = old.Level, Gold = old.Coin, Hp = 100f };
        //             data.SetSection(entry.Key, serializer.Serialize(now));
        //         }
        //     }
        //     // G.Save.RegisterMigration(new V1ToV2());
        //
        // 读档时：存档版本 < 当前 → 逐级跑迁移；缺哪一级 / 存档版本比程序还新 → 明确失败（绝不带错数据进游戏）。
        // 分片自己的载荷也可以自带小版本号（两级版本：根版本管整体结构，分片载荷自己管向后兼容）。
        //
        // 存档文件长这样（persistentDataPath/saves/slot_0.json，缩进 JSON、可直接看）：
        //   { "Version": 1,
        //     "Meta": { "SavedAtUtcTicks": 638... , "Summary": "第 1 关 · 金币 100", "AppVersion": "0.1" },
        //     "Sections": [ { "Key": "player.state", "Payload": "{\"Level\":1,\"Gold\":100,...}" } ] }
        // 每次覆盖写入前，旧档会留成 slot_0.json.bak —— 这就是"主档损坏 → 回退备份"的来源。
        // ───────────────────────────────────────────────────────────────────
    }
}
