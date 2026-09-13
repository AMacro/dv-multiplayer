using Humanizer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Multiplayer.Utils
{
#nullable enable
    public class HashSetQueue<T>
    {
        private readonly Queue<T> queue = new();
        private readonly HashSet<T> set = new();

        public int Count => set.Count;

        public bool Enqueue(T item)
        {
            if (!set.Add(item))
                return false; 

            queue.Enqueue(item);
            return true;
        }

        public T Dequeue()
        {
            T item = queue.Dequeue();
            set.Remove(item);
            return item;
        }

        public bool TryDequeue(out T item)
        {
            if (set.Count == 0)
            {
                item = default!;
                return false;
            }

            item = queue.Dequeue();
            set.Remove(item);
            return true;
        }

        public bool Contains(T item) => set.Contains(item);

        public bool Any() => set.Any();

        public bool Any(System.Func<T,bool> predicate) => set.Any(predicate);

        public void Clear()
        {
            queue.Clear();
            set.Clear();
        }
    }

    public readonly struct CarPlateUpdate(IEnumerable<ushort> carIds, string jobId) : IEquatable<CarPlateUpdate>
    {
        public IReadOnlyCollection<ushort> CarIds { get; } = carIds.ToArray();
        public string JobId { get; } = jobId;

        public bool Equals(CarPlateUpdate other)
        {
            return JobId == other.JobId && CarIds.Count == other.CarIds.Count && new HashSet<ushort>(CarIds).SetEquals(other.CarIds);
        }

        public override bool Equals(object? obj) => ((obj is CarPlateUpdate other) && Equals(other));

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (JobId?.GetHashCode() ?? 0);
                foreach (ushort id in CarIds) hash = (hash * 31) + id.GetHashCode();
                return hash;
            }
        }

        public void Deconstruct(out IReadOnlyCollection<ushort> carIds, out string jobId)
        {
            carIds = CarIds;
            jobId = JobId;
        }
    }
}
