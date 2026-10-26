// SPDX-License-Identifier: MIT
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    // The read frame and comparator 0 used by Linux's timer-imx-sysctr.
    // The firmware initializes the shared 24 MHz / 3 counter before Linux.
    public class IMX8MP_SystemCounter : BasicDoubleWordPeripheral, IKnownSize
    {
        public IMX8MP_SystemCounter(IMachine machine, ulong frequency = 8000000) : base(machine)
        {
            IRQ = new GPIO();
            counter = new LimitTimer(machine.ClockSource, frequency, this, "counter",
                direction: Direction.Ascending, limit: (1UL << 56) - 1, enabled: true);
            comparator = new LimitTimer(machine.ClockSource, frequency, this, "comparator",
                workMode: WorkMode.OneShot, eventEnabled: true);
            comparator.LimitReached += () => { pending = true; UpdateInterrupt(); };
            Registers.CounterLow.Define(this)
                .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => CounterValue & 0xFFFFFFFF);
            Registers.CounterHigh.Define(this)
                .WithValueField(0, 24, FieldMode.Read, valueProviderCallback: _ => CounterValue >> 32)
                .WithReservedBits(24, 8);
            Registers.CompareLow.Define(this)
                .WithValueField(0, 32, out compareLow)
                .WithWriteCallback((_, __) => Rearm());
            Registers.CompareHigh.Define(this)
                .WithValueField(0, 20, out compareHigh)
                .WithReservedBits(20, 12)
                .WithWriteCallback((_, __) => Rearm());
            Registers.CompareControl.Define(this)
                .WithFlag(0, out enabled, name: "EN")
                .WithFlag(1, out masked, name: "IMASK")
                .WithFlag(2, FieldMode.Read, valueProviderCallback: _ => pending, name: "ISTAT")
                .WithReservedBits(3, 29)
                .WithWriteCallback((_, __) => Rearm());
        }

        public override void Reset()
        {
            base.Reset();
            counter.Reset();
            comparator.Reset();
            pending = false;
            IRQ.Unset();
        }

        public GPIO IRQ { get; }
        public long Size => 0x20000;

        private ulong CounterValue
        {
            get
            {
                if(machine.SystemBus.TryGetCurrentCPU(out var cpu))
                {
                    cpu.SyncTime();
                }
                return counter.Value;
            }
        }

        private void Rearm()
        {
            comparator.Enabled = false;
            pending = false;
            if(enabled.Value)
            {
                var target = (compareHigh.Value << 32) | compareLow.Value;
                var now = CounterValue;
                if(target <= now)
                {
                    pending = true;
                }
                else
                {
                    comparator.Limit = target - now;
                    comparator.ResetValue();
                    comparator.Enabled = true;
                }
            }
            UpdateInterrupt();
        }

        private void UpdateInterrupt() => IRQ.Set(enabled.Value && pending && !masked.Value);

        private readonly LimitTimer counter;
        private readonly LimitTimer comparator;
        private IValueRegisterField compareLow, compareHigh;
        private IFlagRegisterField enabled, masked;
        private bool pending;

        private enum Registers : long
        {
            CounterLow = 0x8,
            CounterHigh = 0xC,
            CompareLow = 0x10020,
            CompareHigh = 0x10024,
            CompareControl = 0x1002C,
        }
    }
}
