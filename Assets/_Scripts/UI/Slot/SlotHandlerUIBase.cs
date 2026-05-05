using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UI.Slot
{
    public abstract class SlotHandlerUIBase<TData, TSlot> : SlotInteractHandler where TData : SlotDataBase where TSlot : SlotBase
    {
        [SerializeField] protected TSlot _slotPrefab;
        [SerializeField] protected Transform _parent;

        public TSlot PointedSlot => _pointedSlot as TSlot;

        protected SlotDataContainer<TData> _dataContainer = new();
        protected List<TSlot> _slots = new();

        public virtual void SetData(IEnumerable<TData> datas)
        {
            int index = 0;

            while (_slots.Count < datas.Count())
            {
                CreateSlot();
                index++;
            }

            while (index < _slots.Count)
            {
                Destroy(_slots[index]);
            }

            index = 0;

            foreach (var data in datas)
            {
                _slots[index++].Init(data);
            }

            _dataContainer.SetData(datas);
        }

        public virtual void Add(TData data)
        {
            var slot = CreateSlot();
            slot.Init(data);

            _dataContainer.Add(data);
        }

        public virtual void RemoveAt(int index)
        {
            var slot = _slots[index];
            _slots.RemoveAt(index);
            Destroy(slot);

            _dataContainer.RemoveAt(index);
        }

        protected TSlot CreateSlot()
        {
            var slot = Instantiate(_slotPrefab, _parent);
            _slots.Add(slot);

            return slot;
        }

        protected int GetPointedSlotIndex()
        {
            return _slots.IndexOf(PointedSlot);
        }
    }
}
