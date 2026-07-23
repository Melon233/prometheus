using System.Collections;
using System.Collections.Generic;
using TMPro;

namespace Xuan.Prometheus
{
    public interface IXMap<TKey, TValue>
    {
        void Add(TKey key, TValue value);
        void Remove(TKey key);
        bool TryGet(TKey key, out TValue value);
        bool HasKey(TKey key);
        void Dispose();
    }


    public class XMap<TKey, TValue> : IXMap<TKey, TValue>, IEnumerable<TValue>
    {
        private List<TValue> list = new();
        private Dictionary<TKey, TValue> map = new();

        public IEnumerator<TValue> GetEnumerator()
        {
            return list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void Add(TKey key, TValue value)
        {
            map.Add(key, value);
            list.Add(value);
        }

        public void Remove(TKey key)
        {
            if (map.TryGetValue(key, out var value))
            {
                map.Remove(key);
                list.Remove(value);
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            return map.TryGetValue(key, out value);
        }

        public bool HasKey(TKey key)
        {
            return map.ContainsKey(key);
        }

        public void Dispose()
        {
            map.Clear();
            list.Clear();
        }
    }
}