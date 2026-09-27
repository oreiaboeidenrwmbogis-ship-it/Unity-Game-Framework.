using System;
using System.Collections.Generic;

namespace Template.Core.Pooling
{
    /// <summary>
    /// 纯 C# 类对象池（非 GameObject）—— 用于高频临时对象（事件参数、临时列表、字符串拼接器等），
    /// 把“反复 new + GC”变为复用。GameObject 池请用 <see cref="GameObjectPool"/>（经 PoolService 管理）。
    ///
    /// 术语：Rent = 取出，Return = 归还（对应微软 ArrayPool 惯例）。
    /// 容量策略：Return 时若空闲栈已满则直接丢弃（交给 GC），保证活跃对象总量被容量压住。
    /// </summary>
    public sealed class ObjectPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Stack<T> _stack = new Stack<T>();
        private readonly int _capacity;

        private int _created; // 累计创建数（丢弃后回落，仅统计）

        /// <param name="factory">创建新实例的工厂（如 () =&gt; new List&lt;int&gt;(8)）。</param>
        /// <param name="onRent">取出时回调（如清空/重置对象状态）。</param>
        /// <param name="onReturn">归还时回调（如清空集合）。</param>
        /// <param name="capacity">空闲容量上限（大于等于 1）。</param>
        public ObjectPool(Func<T> factory, Action<T> onRent = null, Action<T> onReturn = null, int capacity = 64)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须 ≥ 1");
            _onRent = onRent;
            _onReturn = onReturn;
            _capacity = capacity;
        }

        /// <summary>空闲数量。</summary>
        public int CountInactive => _stack.Count;

        /// <summary>累计创建总数（含已丢弃）。</summary>
        public int TotalCreated => _created;

        /// <summary>取出一个对象（无空闲时走工厂创建）。</summary>
        public T Rent()
        {
            T item;
            if (_stack.Count > 0)
            {
                item = _stack.Pop();
            }
            else
            {
                _created++;
                item = _factory();
                if (item == null)
                    throw new InvalidOperationException("ObjectPool: 工厂返回了 null，请检查 factory 实现");
            }
            _onRent?.Invoke(item);
            return item;
        }

        /// <summary>归还对象。重复归还同一对象属于调用方 bug（本实现不检测，由 onReturn 契约自行保证）。</summary>
        public void Return(T item)
        {
            if (item == null)
                return;
            _onReturn?.Invoke(item);
            if (_stack.Count >= _capacity)
            {
                _created--; // 超出容量：丢弃，交给 GC
                return;
            }
            _stack.Push(item);
        }

        /// <summary>清空池（丢弃全部空闲对象）。注意：正在使用的对象由调用方负责。</summary>
        public void Clear()
        {
            _stack.Clear();
            _created = 0;
        }
    }
}
