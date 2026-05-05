using System.Collections.Generic;
using System;

namespace UI.Slot
{
    public class SlotDataContainer<TData>
    {
        protected readonly List<TData> _datas = new();

        public IReadOnlyList<TData> Datas => _datas;

        public virtual void SetData(IEnumerable<TData> datas)
        {
            _datas.Clear();
            _datas.AddRange(datas);
        }

        public virtual void Add(TData data)
        {
            int index = Datas.Count;

            _datas.Add(data);
        }

        public virtual void Remove(int index, TData data)
        {
            _datas.RemoveAt(index);
        }

        public virtual void Remove(TData data)
        {
            int index = _datas.LastIndexOf(data);

            if (index < 0 || index >= Datas.Count) return;

            Remove(index, data);
        }

        public virtual void RemoveAt(int index)
        {
            var data = Datas[index];

            Remove(index, data);
        }

        public void Clear()
        {
            _datas.Clear();
        }
    }
}
