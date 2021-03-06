﻿using Phantasma.Mathematics;
using Phantasma.Core.Utils;
using Phantasma.Core;

namespace Phantasma.Blockchain.Contracts.Types
{
    public class Map<K,V>
    {
        private static readonly byte[] count_prefix = "{count}".AsByteArray();
        private readonly byte[] BaseKey;

        public readonly StorageContext Context;

        public V this[K key]
        {
            get
            {
                return Get(key);
            }

            set
            {
                Set(key, value);
            }
        }

        internal Map(StorageContext context, byte[] name, byte[] prefix)
        {
            this.Context = context;
            this.BaseKey = prefix.ConcatBytes(name);
        }

        private byte[] CountKey()
        {
            return BaseKey.ConcatBytes(count_prefix);
        }

        private byte[] ElementKey(K index)
        {
            byte[] bytes = Serialization.Serialize(index);
            return BaseKey.ConcatBytes(bytes);
        }

        public BigInteger Count()
        {
            return Context.Get(CountKey()).AsBigInteger();
        }

        public bool ContainsKey(K key)
        {
            return Context.Has(ElementKey(key));
        }

        public void Set(K key, V value)
        {
            bool exists = ContainsKey(key);
            var bytes = Serialization.Serialize(value);
            Context.Put(ElementKey(key), bytes);

            if (!exists)
            {
                var size = Count() + 1;
                Context.Put(CountKey(), size);
            }
        }

        public V Get(K key)
        {
            if (ContainsKey(key))
            {
                var bytes = Context.Get(ElementKey(key));
                return Serialization.Unserialize<V>(bytes);
            }

            throw new ContractException("key not found");
        }

        public void Remove(K key)
        {
            if (ContainsKey(key))
            {
                Context.Delete(ElementKey(key));
                var size = Count() - 1;
                Context.Put(CountKey(), size);
            }
        }

        public V[] All(K[] keys)
        {
            var size = keys.Length;
            var items = new V[size];
            for (int i = 0; i < size; i++)
            {
                items[i] = Get(keys[i]);
            }
            return items;
        }

    }
}
