﻿using Phantasma.Utils;
using System;

namespace Phantasma.Core
{
    public partial class Chain
    {
        private Transaction GenerateNativeTokenIssueTx(KeyPair owner)
        {
            var script = ScriptUtils.TokenIssueScript("Phantasma", "SOUL", 100000000, 100000000, Contracts.TokenAttribute.Burnable | Contracts.TokenAttribute.Tradable);
            var tx = new Transaction(owner.PublicKey, script, 0, 0);
            tx.Sign(owner);
            return tx;
        }

        private Transaction GenerateDistributionDeployTx(KeyPair owner)
        {
            var script = ScriptUtils.ContractDeployScript(DistributionContract.DefaultScript, DistributionContract.DefaultABI);
            var tx = new Transaction(owner.PublicKey, script, 0, 0);
            tx.Sign(owner);
            return tx;
        }

        private Transaction GenerateFeeGovernanceDeployTx(KeyPair owner)
        {
            var script = ScriptUtils.ContractDeployScript(FeeGovernanceContract.DefaultScript, FeeGovernanceContract.DefaultABI);
            var tx = new Transaction(owner.PublicKey, script, 0, 0);
            tx.Sign(owner);
            return tx;
        }

        private Block CreateGenesisBlock(KeyPair owner)
        {
            var issueTx = GenerateNativeTokenIssueTx(owner);
            var distTx = GenerateDistributionDeployTx(owner);
            var feeGovTx = GenerateDistributionDeployTx(owner);
            var block = new Block(DateTime.UtcNow.ToTimestamp(), owner.PublicKey, new Transaction[] { issueTx, distTx, feeGovTx });

            return block;
        }

    }
}