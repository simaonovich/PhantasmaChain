﻿using Phantasma.Mathematics;
using Phantasma.Cryptography;
using Phantasma.Core.Utils;

namespace Phantasma.VM.Utils
{
    public static class ScriptUtils
    {
        public static byte[] CallContractScript(Address contract, string method, params object[] args)
        {
            var sb = new ScriptBuilder();
            byte dest_reg = 1;
            sb.Emit(VM.Opcode.CTX, new byte[] { dest_reg }.ConcatBytes(contract.PublicKey));

            byte temp_reg = 0;

            for (int i=args.Length-1; i>=0; i--)
            {
                var arg = args[i];

                if (arg is string)
                {
                    sb.EmitLoad(temp_reg, (string)arg);
                    sb.EmitPush(temp_reg);
                }
                else
                if (arg is BigInteger)
                {
                    sb.EmitLoad(temp_reg, (BigInteger)arg);
                    sb.EmitPush(temp_reg);
                }
                else
                if (arg is bool)
                {
                    sb.EmitLoad(temp_reg, (bool)arg);
                    sb.EmitPush(temp_reg);
                }
                else
                if (arg is byte[])
                {
                    sb.EmitLoad(temp_reg, (byte[])arg, VMType.Bytes);
                    sb.EmitPush(temp_reg);
                }
                else
                if (arg is Address)
                {
                    sb.EmitLoad(temp_reg, ((Address)arg).PublicKey, VMType.Bytes);
                    sb.EmitPush(temp_reg);
                    sb.EmitExtCall("Address()", temp_reg);
                }
                else
                {
                    throw new System.Exception("invalid type");
                }
            }

            sb.EmitLoad(temp_reg, method);
            sb.EmitPush(temp_reg);
            sb.Emit(VM.Opcode.SWITCH, new byte[] { dest_reg });
            sb.Emit(VM.Opcode.RET);
            return sb.ToScript();
        }

        public static byte[] TokenMintScript(Address token, Address target, BigInteger amount)
        {
            return CallContractScript(token, "Mint", target, amount);
        }

        public static byte[] TokenTransferScript(Address token, Address from, Address to, BigInteger amount)
        {
            return CallContractScript(token, "Transfer", from, to, amount);
        }

        public static byte[] ContractDeployScript(byte[] script, byte[] abi)
        {
            var sb = new ScriptBuilder();

            sb.EmitLoad(0, script);
            sb.EmitLoad(1, abi);

            sb.EmitExtCall("Chain.Deploy");
            sb.Emit(VM.Opcode.RET);

            return sb.ToScript();
        }
    }
}
