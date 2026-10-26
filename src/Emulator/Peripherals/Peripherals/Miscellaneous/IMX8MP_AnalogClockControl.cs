// SPDX-License-Identifier: MIT
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Logical PLL control used by the i.MX8MP clock driver. Reset represents
    // clocks initialized by the skipped SPL, rather than analog power-on state.
    // No analog settling delay or propagation into peripheral timing is modeled.
    public class IMX8MP_AnalogClockControl : BasicDoubleWordPeripheral, IKnownSize
    {
        public IMX8MP_AnalogClockControl(IMachine machine) : base(machine)
        {
            // Auxiliary PLLs start disabled, with valid nominal dividers.
            // Linux can program/prepare them without polling absent registers.
            AddPll("AUDIO1", 0x0, 0, 0x106023, true, 9437);
            AddPll("AUDIO2", 0x14, 0, 0x169033, true, 17511);
            AddPll("VIDEO1", 0x28, 0, 0x0C6022, true);
            AddPll("DRAM", 0x50, 0x2200, 0x190032, true);
            AddPll("GPU", 0x64, 0, 0x12C032);
            AddPll("VPU", 0x74, 0, 0x12C032);
            AddPll("ARM", 0x84, 0x0A00, 0x0C8021);
            AddPll("SYS1", 0x94, 0x0A00, 0x190032);
            AddPll("SYS2", 0x104, 0x0A00, 0x0FA031);
            AddPll("SYS3", 0x114, 0x0A00, 0x12C032);
            // Read-only SoC identity already used by the example platform.
            RegistersCollection.DefineRegister(0x800, 0x00824311)
                .WithValueField(0, 32, FieldMode.Read, name: "DIGPROG");
        }

        public ulong GetFrequency(string pllName)
        {
            var pll = plls[pllName];
            var outputBit = pll.Fractional ? 13 : 11;
            if((pll.Control.Value & (1UL << outputBit)) == 0)
            {
                return 0;
            }
            if((pll.Control.Value & (1UL << 4)) != 0)
            {
                return (pll.Control.Value & 3) == 0 ? ReferenceFrequency : 0;
            }
            return IsLocked(pll) ? VcoOutput(pll) : 0;
        }

        public long Size => 0x1000;

        private void AddPll(string name, long offset, uint control, uint divider, bool fractional = false, ushort fraction = 0)
        {
            var pll = new Pll { Fractional = fractional };
            plls.Add(name, pll);
            RegistersCollection.DefineRegister(offset, control)
                .WithValueField(0, 31, out pll.Control, name: name + "_CONTROL")
                .WithFlag(31, FieldMode.Read, valueProviderCallback: _ => IsLocked(pll), name: "LOCK");
            RegistersCollection.DefineRegister(offset + 4, divider)
                .WithValueField(0, 3, name: "SDIV")
                .WithReservedBits(3, 1)
                .WithValueField(4, 6, name: "PDIV")
                .WithReservedBits(10, 2)
                .WithValueField(12, 10, name: "MDIV")
                .WithReservedBits(22, 10);
            // Read the divider via its register so there is one stored value.
            pll.DividerOffset = offset + 4;
            if(fractional)
            {
                RegistersCollection.DefineRegister(offset + 8, fraction)
                    .WithValueField(0, 16, out pll.Fraction, name: "KDIV")
                    .WithReservedBits(16, 16);
            }
        }

        private bool IsLocked(Pll pll) => (pll.Control.Value & 3) == 0
            && (pll.Control.Value & (1UL << 9)) != 0 && VcoOutput(pll) != 0;

        private ulong VcoOutput(Pll pll)
        {
            var divider = RegistersCollection.Read(pll.DividerOffset);
            var m = (divider >> 12) & 0x3FF;
            var p = (divider >> 4) & 0x3F;
            var s = divider & 7;
            var scaledM = (long)m * 65536 + (pll.Fractional ? (short)pll.Fraction.Value : 0);
            if(p == 0 || scaledM <= 0)
            {
                return 0;
            }
            return ReferenceFrequency * (ulong)scaledM / ((ulong)p * 65536 * (1UL << (int)s));
        }

        private class Pll
        {
            public IValueRegisterField Control, Fraction;
            public long DividerOffset;
            public bool Fractional;
        }

        private readonly Dictionary<string, Pll> plls = new Dictionary<string, Pll>();
        private const ulong ReferenceFrequency = 24000000;
    }
}
