﻿using System.Collections.Generic;
using System.IO;
using Phantasma.Cryptography;
using Phantasma.Mathematics;
using Phantasma.VM.Contracts;
using Phantasma.Core.Types;
using Phantasma.Core.Utils;
using Phantasma.Blockchain.Contracts;

namespace Phantasma.Blockchain
{
    public sealed class Block: IBlock
    {
        public static readonly BigInteger InitialDifficulty = 127;
        public static readonly float IdealBlockTime = 5;
        public static readonly float BlockTimeFlutuation = 0.2f;

        public readonly Address MinerAddress;
        public readonly Address TokenAddress;

        public BigInteger Height { get; private set; }
        public Timestamp Timestamp { get; private set; }
        public uint Nonce { get; private set; }
        public Hash Hash { get; private set; }
        public Hash PreviousHash { get; private set; }

        public readonly BigInteger difficulty;

        private List<Transaction> _transactions;
        public IEnumerable<ITransaction> Transactions => _transactions;

        public List<Event> Events = new List<Event>();

        public Block(Timestamp timestamp, Address minerAddress, Address tokenAddress, IEnumerable<Transaction> transactions, Block previous = null)
        {
            this.Height = previous != null ? previous.Height + 1 : 0;
            this.PreviousHash = previous != null ? previous.Hash : null;
            this.Timestamp = timestamp;
            this.MinerAddress = minerAddress;
            this.TokenAddress = tokenAddress;

            _transactions = new List<Transaction>();
            foreach (var tx in transactions)
            {
                _transactions.Add(tx);
            }

            if (previous != null)
            {
                var delta = this.Timestamp - previous.Timestamp;

                if (delta < IdealBlockTime * (1.0f - BlockTimeFlutuation))
                {
                    this.difficulty = previous.difficulty - 1;
                }
                else
                if (delta > IdealBlockTime * (1.0f + BlockTimeFlutuation))
                {
                    this.difficulty = previous.difficulty - 1;
                }
                else {
                    this.difficulty = previous.difficulty;
                }
            }
            else
            {
                this.difficulty = InitialDifficulty;
            }

            this.UpdateHash(0);
        }

        private byte[] ToArray()
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    Serialize(writer);
                }

                return stream.ToArray();
            }
        }

        internal void Notify(Event evt)
        {
            this.Events.Add(evt);
        }

        // TODO - Optimize this to avoid recalculating the arrays if only the nonce changed
        internal void UpdateHash(uint nonce)
        {
            this.Nonce = nonce;
            var data = ToArray();
            var hashBytes = CryptoExtensions.Sha256(data);
            this.Hash = new Hash(hashBytes);
        }

        #region SERIALIZATION

        internal void Serialize(BinaryWriter writer) {
            writer.WriteBigInteger(Height);
            writer.Write(Timestamp.Value);
            writer.WriteHash(PreviousHash);
            writer.WriteAddress(MinerAddress);
            writer.WriteAddress(TokenAddress);
            writer.Write((ushort)Events.Count);
            foreach (var evt in Events)
            {
                evt.Serialize(writer);
            }
            writer.Write(Nonce);
        }

        internal static Block Unserialize(BinaryReader reader) {
            var height = reader.ReadBigInteger();
            var timestamp = new Timestamp(reader.ReadUInt32());
            var prevHash = reader.ReadHash();
            var minerAddress =  reader.ReadAddress();
            var tokenAddress = reader.ReadAddress();

            var evtCount = reader.ReadUInt16();
            var evts = new Event[evtCount];
            for (int i=0;i<evtCount; i++)
            {
                evts[i] = Event.Unserialize(reader); 
            }
            var nonce = reader.ReadUInt32();
            return new Block(timestamp, minerAddress, tokenAddress, null);
        }
        #endregion
    }
}
