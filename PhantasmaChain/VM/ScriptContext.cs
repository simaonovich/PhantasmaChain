﻿using System;
using System.Collections.Generic;
using Phantasma.Core;
using Phantasma.Mathematics;
using Phantasma.Cryptography;

namespace Phantasma.VM
{
    class ScriptContext: ExecutionContext
    {
        public byte[] Script { get; private set; }

        public uint InstructionPointer { get; private set; }
        public ExecutionState State { get; private set; }

        public ScriptContext(byte[] script)
        {
            this.Script = script;
            this.InstructionPointer = 0;
            this.State = ExecutionState.Running;
        }

        public override int GetSize()
        {
            return this.Script.Length;
        }

        public override ExecutionState Execute(ExecutionFrame frame, Stack<VMObject> stack)
        {
            while (State == ExecutionState.Running)
            {
                this.Step(frame, stack);
            }

            return State;
        }

        #region IO 
        private byte Read8()
        {
            Throw.If(InstructionPointer >= this.Script.Length, "Outside of range");

            var result = this.Script[InstructionPointer];
            InstructionPointer++;
            return result;
        }

        private ushort Read16()
        {
            var a = Read8();
            var b = Read8();
            return (ushort)(a + (b >> 8));
        }

        private uint Read32()
        {
            var a = Read8();
            var b = Read8();
            var c = Read8();
            var d = Read8();
            return (uint)(a + (b << 8) + (c << 16) + (d << 24));
        }

        private ulong Read64()
        {
            var a = Read8();
            var b = Read8();
            var c = Read8();
            var d = Read8();
            var e = Read8();
            var f = Read8();
            var g = Read8();
            var h = Read8();
            return (ulong)(a + (b << 8) + (c << 16) + (d << 24) + (e << 32) + (f << 40) + (g << 48) + (g << 56));
        }

        private ulong ReadVar(ulong max)
        {
            byte n = Read8();

            ulong val;

            switch (n)
            {
                case 0xFD: val = Read16(); break;
                case 0xFE: val = Read32(); break;
                case 0xFF: val = Read64(); break;
                default: val = n; break;
            }

            Throw.If(val > max, "Input exceed max");

            return val;
        }

        private byte[] ReadBytes(int length)
        {
            Throw.If(InstructionPointer + length >= this.Script.Length, "Outside of range");

            var result = new byte[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = this.Script[InstructionPointer];
                InstructionPointer++;
            }

            return result;
        }
        #endregion

        private void Expect(bool assertion)
        {
            Throw.If(!assertion, "Assertion failed");
        }

        private void SetState(ExecutionState state)
        {
            this.State = state;
        }

        public void Step(ExecutionFrame currentFrame, Stack<VMObject> stack)
        {
            try
            {
                var opcode = (Opcode)Read8();

                switch (opcode)
                {
                    case Opcode.NOP:
                        {
                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.MOVE:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            currentFrame.Registers[dst] = currentFrame.Registers[src];
                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.COPY:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            currentFrame.Registers[dst].Copy(currentFrame.Registers[src]);
                            break;
                        }

                    // args: byte dst_reg, byte type, var length, var data_bytes
                    case Opcode.LOAD:
                        {
                            var dst = Read8();
                            var type = (VMType)Read8();
                            var len = (int)ReadVar(0xFFF);

                            Expect(dst < currentFrame.Registers.Length);

                            var bytes = ReadBytes(len);
                            currentFrame.Registers[dst].SetValue(bytes, type);

                            break;
                        }

                    // args: byte src_reg
                    case Opcode.PUSH:
                        {
                            var src = Read8();
                            Expect(src < currentFrame.Registers.Length);

                            var temp = new VMObject();
                            temp.Copy(currentFrame.Registers[src]);
                            stack.Push(temp);
                            break;
                        }

                    // args: byte dest_reg
                    case Opcode.POP:
                        {
                            var dst = Read8();

                            Expect(stack.Count > 0);
                            Expect(dst < currentFrame.Registers.Length);

                            currentFrame.Registers[dst] = stack.Pop();
                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.SWAP:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var temp = currentFrame.Registers[src];
                            currentFrame.Registers[src] = currentFrame.Registers[dst];
                            currentFrame.Registers[dst] = temp;

                            break;
                        }

                    // args: ushort offset, byte regCount
                    case Opcode.CALL:
                        {
                            var count = Read8();
                            var ofs = Read16();

                            Expect(ofs < this.Script.Length);
                            Expect(count >= 1);
                            Expect(count <= VirtualMachine.MaxRegisterCount);

                            currentFrame.VM.PushFrame(this, InstructionPointer, count);

                            InstructionPointer = ofs;
                            break;
                        }

                    // args: byte srcReg
                    case Opcode.EXTCALL:
                        {
                            var src = Read8();
                            Expect(src < currentFrame.Registers.Length);

                            var method = currentFrame.Registers[src].AsString();

                            var state = currentFrame.VM.ExecuteInterop(method);
                            if (state != ExecutionState.Running)
                            {
                                return;
                            }

                            break;
                        }

                    // args: ushort offset, byte src_reg
                    // NOTE: JMP only has offset arg, not the rest
                    case Opcode.JMP:
                    case Opcode.JMPIF:
                    case Opcode.JMPNOT:
                        {
                            bool shouldJump;

                            if (opcode == Opcode.JMP)
                            {
                                shouldJump = true;
                            }
                            else
                            {
                                var src = Read8();
                                Expect(src < currentFrame.Registers.Length);

                                shouldJump = currentFrame.Registers[src].AsBool();

                                if (opcode == Opcode.JMPNOT)
                                {
                                    shouldJump = !shouldJump;
                                }
                            }

                            var newPos = (short)Read16();

                            Expect(newPos >= 0);
                            Expect(newPos < this.Script.Length);

                            if (shouldJump)
                            {
                                InstructionPointer = (uint)newPos;
                            }

                            break;
                        }

                    // args: var length, var bytes
                    case Opcode.THROW:
                        {
                            var len = (int)ReadVar(1024);
                            if (len > 0)
                            {
                                var bytes = ReadBytes(len);
                            }

                            SetState(ExecutionState.Fault);
                            return;
                        }

                    // args: none
                    case Opcode.RET:
                        {
                            if (currentFrame.VM.frames.Count > 1)
                            {
                                InstructionPointer = currentFrame.VM.PopFrame();
                                Expect(currentFrame.VM.currentContext == this);
                                Expect(InstructionPointer < this.Script.Length);
                            }
                            else
                            {
                                SetState(ExecutionState.Halt);
                            }
                            return;
                        }

                    // args: byte src_a_reg, byte src_b_reg, byte dest_reg
                    case Opcode.CAT:
                        {
                            var srcA = Read8();
                            var srcB = Read8();
                            var dst = Read8();

                            Expect(srcA < currentFrame.Registers.Length);
                            Expect(srcB < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var A = currentFrame.Registers[srcA];
                            var B = currentFrame.Registers[srcB];

                            if (!A.IsEmpty)
                            {
                                if (B.IsEmpty)
                                {
                                    currentFrame.Registers[dst].Copy(A);
                                }
                                else
                                {
                                    var bytesA = A.AsByteArray();
                                    var bytesB = B.AsByteArray();

                                    var result = new byte[bytesA.Length + bytesB.Length];
                                    Array.Copy(bytesA, result, bytesA.Length);
                                    Array.Copy(bytesB, 0, result, bytesA.Length, bytesB.Length);

                                    currentFrame.Registers[dst].SetValue(result, VMType.Bytes);
                                }
                            }
                            else
                            {
                                if (B.IsEmpty)
                                {
                                    currentFrame.Registers[dst] = new VMObject();
                                }
                                else
                                {
                                    currentFrame.Registers[dst].Copy(B);
                                }
                            }

                            break;
                        }

                    case Opcode.SUBSTR:
                        {
                            throw new NotImplementedException();
                        }

                    // args: byte src_reg, byte dest_reg, var length
                    case Opcode.LEFT:
                        {
                            var src = Read8();
                            var dst = Read8();
                            var len = (int)ReadVar(0xFFFF);

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var src_array = currentFrame.Registers[src].AsByteArray();
                            Expect(len <= src_array.Length);

                            var result = new byte[len];

                            Array.Copy(src_array, result, len);

                            currentFrame.Registers[dst].SetValue(result, VMType.Bytes);
                            break;
                        }

                    // args: byte src_reg, byte dest_reg, byte length
                    case Opcode.RIGHT:
                        {
                            var src = Read8();
                            var dst = Read8();
                            var len = (int)ReadVar(0xFFFF);

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var src_array = currentFrame.Registers[src].AsByteArray();
                            Expect(len <= src_array.Length);

                            var ofs = src_array.Length - len;

                            var result = new byte[len];
                            Array.Copy(src_array, ofs, result, 0, len);

                            currentFrame.Registers[dst].SetValue(result, VMType.Bytes);
                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.SIZE:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var src_array = currentFrame.Registers[src].AsByteArray();
                            currentFrame.Registers[dst].SetValue(src_array.Length);
                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.NOT:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[src].AsBool();

                            currentFrame.Registers[dst].SetValue(!val);
                            break;
                        }

                    // args: byte src_a_reg, byte src_b_reg, byte dest_reg
                    case Opcode.AND:
                    case Opcode.OR:
                    case Opcode.XOR:
                        {
                            var srcA = Read8();
                            var srcB = Read8();
                            var dst = Read8();

                            Expect(srcA < currentFrame.Registers.Length);
                            Expect(srcB < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var a = currentFrame.Registers[srcA].AsBool();
                            var b = currentFrame.Registers[srcB].AsBool();

                            bool result;
                            switch (opcode)
                            {
                                case Opcode.AND: result = (a && b); break;
                                case Opcode.OR: result = (a || b); break;
                                case Opcode.XOR: result = (a ^ b); break;
                                default:
                                    {
                                        SetState(ExecutionState.Fault);
                                        return;
                                    }
                            }

                            currentFrame.Registers[dst].SetValue(result);
                            break;
                        }

                    // args: byte src_a_reg, byte src_b_reg, byte dest_reg
                    case Opcode.EQUAL:
                        {
                            var srcA = Read8();
                            var srcB = Read8();
                            var dst = Read8();

                            Expect(srcA < currentFrame.Registers.Length);
                            Expect(srcB < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var a = currentFrame.Registers[srcA];
                            var b = currentFrame.Registers[srcB];

                            var result = a.Equals(b);
                            currentFrame.Registers[dst].SetValue(result);

                            break;
                        }

                    // args: byte src_a_reg, byte src_b_reg, byte dest_reg
                    case Opcode.LT:
                    case Opcode.GT:
                    case Opcode.LTE:
                    case Opcode.GTE:
                        {
                            var srcA = Read8();
                            var srcB = Read8();
                            var dst = Read8();

                            Expect(srcA < currentFrame.Registers.Length);
                            Expect(srcB < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var a = currentFrame.Registers[srcA].AsNumber();
                            var b = currentFrame.Registers[srcB].AsNumber();

                            bool result;
                            switch (opcode)
                            {
                                case Opcode.LT: result = (a < b); break;
                                case Opcode.GT: result = (a > b); break;
                                case Opcode.LTE: result = (a <= b); break;
                                case Opcode.GTE: result = (a >= b); break;
                                default:
                                    {
                                        SetState(ExecutionState.Fault);
                                        return;
                                    }
                            }

                            currentFrame.Registers[dst].SetValue(result);
                            break;
                        }

                    // args: byte reg
                    case Opcode.INC:
                        {
                            var dst = Read8();
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[dst].AsNumber();
                            currentFrame.Registers[dst].SetValue(val + 1);

                            break;
                        }

                    // args: byte reg
                    case Opcode.DEC:
                        {
                            var dst = Read8();
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[dst].AsNumber();
                            currentFrame.Registers[dst].SetValue(val - 1);

                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.SIGN:
                        {
                            var src = Read8();
                            var dst = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[src].AsNumber();

                            if (val == 0)
                            {
                                currentFrame.Registers[dst].SetValue(0);
                            }
                            else
                            {
                                currentFrame.Registers[dst].SetValue(val < 0 ? -1 : 1);
                            }

                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.NEGATE:
                        {
                            var src = Read8();
                            var dst = Read8();
                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[src].AsNumber();
                            currentFrame.Registers[dst].SetValue(-val);

                            break;
                        }

                    // args: byte src_reg, byte dest_reg
                    case Opcode.ABS:
                        {
                            var src = Read8();
                            var dst = Read8();
                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var val = currentFrame.Registers[src].AsNumber();
                            currentFrame.Registers[dst].SetValue(val < 0 ? -val : val);

                            break;
                        }

                    // args: byte src_a_reg, byte src_b_reg, byte dest_reg
                    case Opcode.ADD:
                    case Opcode.SUB:
                    case Opcode.MUL:
                    case Opcode.DIV:
                    case Opcode.MOD:
                    case Opcode.SHR:
                    case Opcode.SHL:
                    case Opcode.MIN:
                    case Opcode.MAX:
                        {
                            var srcA = Read8();
                            var srcB = Read8();
                            var dst = Read8();

                            Expect(srcA < currentFrame.Registers.Length);
                            Expect(srcB < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);

                            var a = currentFrame.Registers[srcA].AsNumber();
                            var b = currentFrame.Registers[srcB].AsNumber();

                            BigInteger result;

                            switch (opcode)
                            {
                                case Opcode.ADD: result = a + b; break;
                                case Opcode.SUB: result = a - b; break;
                                case Opcode.MUL: result = a * b; break;
                                case Opcode.DIV: result = a / b; break;
                                case Opcode.MOD: result = a % b; break;
                                case Opcode.SHR: result = a >> (int)b; break;
                                case Opcode.SHL: result = a << (int)b; break;
                                case Opcode.MIN: result = a < b ? a : b; break;
                                case Opcode.MAX: result = a > b ? a : b; break;
                                default:
                                    {
                                        SetState(ExecutionState.Fault);
                                        return;
                                    }
                            }

                            currentFrame.Registers[dst].SetValue(result);
                            break;
                        }

                    // args: byte src_reg, byte dest_reg, byte key
                    case Opcode.PUT:
                        {
                            var src = Read8();
                            var dst = Read8();
                            var key = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);
                            Expect(key < currentFrame.Registers.Length);

                            currentFrame.Registers[dst].SetKey(currentFrame.Registers[key].AsString(), currentFrame.Registers[src]);

                            break;
                        }

                    // args: byte src_reg, byte dest_reg, byte key
                    case Opcode.GET:
                        {
                            var src = Read8();
                            var dst = Read8();
                            var key = Read8();

                            Expect(src < currentFrame.Registers.Length);
                            Expect(dst < currentFrame.Registers.Length);
                            Expect(key < currentFrame.Registers.Length);

                            currentFrame.Registers[dst] = currentFrame.Registers[src].GetKey(currentFrame.Registers[key].AsString());

                            break;
                        }

                    // args: byte dest_reg
                    case Opcode.THIS:
                        {
                            var dst = Read8();
                            Expect(dst < currentFrame.Registers.Length);

                            currentFrame.Registers[dst].SetValue(this);

                            break;
                        }

                    // args: byte dest_reg, var key
                    case Opcode.CTX:
                        {
                            var dst = Read8();
                            var key = ReadBytes(Address.PublicKeyLength);

                            Expect(dst < currentFrame.Registers.Length);

                            ExecutionContext context = currentFrame.VM.FindContext(new Address(key));

                            if (context == null)
                            {
                                SetState(ExecutionState.Fault);
                            }

                            currentFrame.Registers[dst].SetValue(context);

                            break;
                        }

                    // args: byte src_reg
                    case Opcode.SWITCH:
                        {
                            var src = Read8();
                            Expect(src < currentFrame.Registers.Length);

                            var context = currentFrame.Registers[src].AsInterop<ExecutionContext>();

                            State = currentFrame.VM.SwitchContext(context);

                            if (State == ExecutionState.Running)
                            {
                                currentFrame.VM.PopFrame();
                            }

                            break;
                        }

                    default:
                        {
                            SetState(ExecutionState.Fault);
                            return;
                        }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex.ToString());
                SetState(ExecutionState.Fault);
            }
        }

    }
}
