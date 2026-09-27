using System.Text;
using Template.Core.Logging;
using Template.Core.Services;
using UnityEngine;

namespace Template.Config
{
    /// <summary>
    /// 配置服务：持有 <see cref="ConfigDatabase"/>，作为业务"取配置"的统一入口。
    ///
    /// 装配方式（阶段 3.1）：Boot 场景挂 <see cref="ConfigInstaller"/>，把配置库交进来；
    /// 阶段 3.3 接入 Addressables 后本服务增加"按 address 异步加载配置库"的路径 ——
    /// 业务侧 API（<c>Configs.Item.Get(1001)</c> / <see cref="GetTable{T}"/>）不变。
    ///
    /// 配置表是**只读资产**：本服务不做运行期增删改（那是存档系统的事）。
    /// </summary>
    public sealed class ConfigService : IGameService
    {
        /// <summary>当前配置库；未装配时为 null。</summary>
        public ConfigDatabase Database { get; private set; }

        /// <summary>配置库是否已装配。</summary>
        public bool IsReady => Database != null;

        public void Init()
        {
            if (Database == null)
                Log.Warn("Config", "配置服务就绪，但尚未装配配置库 —— Boot 场景挂 ConfigInstaller 即可（或稍后调用 SetDatabase）");
            else
                Log.Info("Config", "配置服务就绪：{0} 张表", Database.Tables.Count);
        }

        public void Dispose() => Database = null;

        /// <summary>装配配置库（Boot 阶段调用；同一次运行里重复装配会告警）。</summary>
        public void SetDatabase(ConfigDatabase database)
        {
            if (database == null)
            {
                Log.Error("Config", "SetDatabase: 配置库为空（Inspector 引用丢了？）");
                return;
            }
            if (Database != null && Database != database)
                Log.Warn("Config", "配置库被替换：{0} → {1}", Database.name, database.name);

            Database = database;
            Log.Info("Config", "配置库已装配：{0} 张表 [{1}]", database.Tables.Count, DescribeTables(database));
        }

        /// <summary>按表类型取表；不存在返回 null（调用方自行降级）。</summary>
        public TTable GetTable<TTable>() where TTable : ScriptableObject
            => Database != null ? Database.GetTable<TTable>() : null;

        /// <summary>按表类型取表；不存在记 Error 日志（"这里必须有"的场合用它）。</summary>
        public TTable RequireTable<TTable>() where TTable : ScriptableObject
        {
            if (Database == null)
            {
                Log.Error("Config", "配置库未装配，取表失败 <{0}>", typeof(TTable).Name);
                return null;
            }
            return Database.RequireTable<TTable>();
        }

        private static string DescribeTables(ConfigDatabase database)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < database.Tables.Count; i++)
            {
                if (i > 0)
                    sb.Append('、');
                sb.Append(database.Tables[i] != null ? database.Tables[i].name : "(空)");
            }
            return sb.ToString();
        }
    }
}
