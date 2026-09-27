using System.Collections.Generic;
using Template.Core.Logging;

namespace Template.Save
{
    /// <summary>
    /// 版本迁移链：把任意老版本存档逐级跑 <see cref="ISaveMigration"/> 升到目标版本。
    /// 单独成类是为了**可测**（目标版本可注入，测试里能构造 v1→v2→v3 的完整链），
    /// <see cref="SaveService"/> 只持有它并转交注册。
    ///
    /// 策略：缺步、或存档版本高于目标版本 → **宁可读档失败也不带病进游戏**
    /// （错位的数据比"读档失败提示"危险得多）。
    /// </summary>
    public sealed class SaveMigrator
    {
        private const int MaxSteps = 64; // 防成环兜底

        private readonly List<ISaveMigration> _steps = new List<ISaveMigration>();

        /// <summary>目标版本（跑到这个版本为止）。</summary>
        public int TargetVersion { get; }

        /// <summary>已注册的迁移步数。</summary>
        public int Count => _steps.Count;

        public SaveMigrator(int targetVersion)
        {
            TargetVersion = targetVersion;
        }

        /// <summary>注册一步迁移（同一 FromVersion 重复注册会被拒绝）。</summary>
        public bool Register(ISaveMigration migration)
        {
            if (migration == null)
                return false;

            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].FromVersion == migration.FromVersion)
                {
                    Log.Error("Save", "迁移重复注册：v{0} → v{1}（每步只能有一个所有者）",
                        migration.FromVersion, migration.FromVersion + 1);
                    return false;
                }
            }
            _steps.Add(migration);
            return true;
        }

        /// <summary>就地升级存档到 <see cref="TargetVersion"/>；失败时返回 false 并给出原因。</summary>
        public bool TryMigrate(SaveData data, ISaveSerializer serializer, out string error)
        {
            error = null;

            if (data.Version == TargetVersion)
                return true;

            if (data.Version > TargetVersion)
            {
                error = $"存档版本 v{data.Version} 高于程序支持的 v{TargetVersion}（用更新的客户端存过档？）";
                Log.Error("Save", "{0}", error);
                return false;
            }

            int guard = 0;
            while (data.Version < TargetVersion)
            {
                ISaveMigration step = Find(data.Version);
                if (step == null)
                {
                    error = $"缺少 v{data.Version} → v{data.Version + 1} 的迁移（存档无法升级到当前版本）";
                    Log.Error("Save", "{0}", error);
                    return false;
                }

                int from = data.Version;
                step.Apply(data, serializer);
                data.Version = from + 1;
                Log.Info("Save", "存档迁移：v{0} → v{1}", from, data.Version);

                if (++guard > MaxSteps)
                {
                    error = $"迁移链疑似成环（连续跑了 {MaxSteps} 步仍未到 v{TargetVersion}）";
                    Log.Error("Save", "{0}", error);
                    return false;
                }
            }
            return true;
        }

        private ISaveMigration Find(int fromVersion)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].FromVersion == fromVersion)
                    return _steps[i];
            }
            return null;
        }
    }
}
