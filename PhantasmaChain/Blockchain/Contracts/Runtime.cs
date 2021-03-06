﻿using System;
using System.Collections.Generic;
using Phantasma.VM.Contracts;
using Phantasma.VM;
using Phantasma.Cryptography;

namespace Phantasma.Blockchain.Contracts
{
    public class RuntimeVM : VirtualMachine
    {
        public Transaction Transaction { get; }
        public Chain Chain { get; private set; }

        public RuntimeVM(Chain chain, Transaction tx) : base(tx.Script)
        {
            this.Transaction = tx;
            this.Chain = chain;

            chain.RegisterInterop(this);
        }

        internal void RegisterMethod(string name, Func<VirtualMachine, ExecutionState> handler)
        {
            handlers[name] = handler;
        }

        private Dictionary<string, Func<VirtualMachine, ExecutionState>> handlers = new Dictionary<string, Func<VirtualMachine, ExecutionState>>();

        public override ExecutionState ExecuteInterop(string method)
        {
            if (handlers.ContainsKey(method))
            {
                return handlers[method](this);
            }

            return ExecutionState.Fault;
        }

        public T GetContract<T>(Address address) where T : IContract
        {
            throw new System.NotImplementedException();
        }

        public override ExecutionContext LoadContext(Address address)
        {
            foreach (var entry in Chain.NativeContexts)
            {
                if (entry.Contract.Address == address)
                {
                    var storage = this.Chain.FindStorage(address);
                    entry.Contract.SetData(this.Chain, this.Transaction, storage);
                    return entry;
                }
            }

            return null;
        }
    }
}
