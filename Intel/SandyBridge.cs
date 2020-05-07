﻿using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class SandyBridge : ModernIntelCpu
    {
        public SandyBridge()
        {
            coreMonitoringConfigs = new MonitoringConfig[3];
            coreMonitoringConfigs[0] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[1] = new OpCachePerformance(this);
            coreMonitoringConfigs[2] = new ALUPortUtilization(this);
            architectureName = "Sandy Bridge";
        }

        public class OpCachePerformance : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Op Cache Performance"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "Op Cache Ops/C", "Op Cache Hitrate", "Decoder Ops/C", "Op Cache Ops", "Decoder Ops" };

            public OpCachePerformance(SandyBridge intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnablePerformanceCounters();

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // Set PMC0 to count DSB (decoded stream buffer = op cache) uops
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0x79, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count cycles when the DSB's delivering to IDQ (cmask=1)
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0x79, 0x08, true, true, false, false, false, false, true, false, 1);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count MITE (micro instruction translation engine = decoder) uops
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0x79, 0x04, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);

                    // Set PMC3 to count MITE cycles (cmask=1)
                    ulong notTakenBranches = GetPerfEvtSelRegisterValue(0x79, 0x04, true, true, false, false, false, false, true, false, 1);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, notTakenBranches);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalDsbUops = 0;
                ulong totalDsbCycles = 0;
                ulong totalMiteUops = 0;
                ulong totalMiteCycles = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong dsbUops, dsbCycles, miteUops, miteCycles;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out dsbUops);
                    Ring0.ReadMsr(IA32_A_PMC1, out dsbCycles);
                    Ring0.ReadMsr(IA32_A_PMC2, out miteUops);
                    Ring0.ReadMsr(IA32_A_PMC3, out miteCycles);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);
                    Ring0.WriteMsr(IA32_A_PMC3, 0);

                    totalDsbUops += dsbUops;
                    totalDsbCycles += dsbCycles;
                    totalMiteUops += miteUops;
                    totalMiteCycles += miteCycles;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float dsbThroughput = (float)dsbUops / dsbCycles;
                    float dsbHitrate = (float)dsbUops / (dsbUops + miteUops) * 100;
                    float miteThroughput = (float)miteUops / miteCycles;
                    float threadIpc = (float)retiredInstructions / activeCycles;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}", dsbThroughput),
                        string.Format("{0:F2}%", dsbHitrate),
                        string.Format("{0:F2}", miteThroughput),
                        FormatLargeNumber(dsbUops),
                        FormatLargeNumber(miteUops)};
                }

                float overallDsbThroughput = (float)totalDsbUops / totalDsbCycles;
                float overallDsbHitrate = (float)totalDsbUops / (totalDsbUops + totalMiteUops) * 100;
                float overallMiteThroughput = (float)totalMiteUops / totalMiteCycles;
                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber(totalRetiredInstructions),
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}", overallDsbThroughput),
                    string.Format("{0:F2}%", overallDsbHitrate),
                    string.Format("{0:F2}", overallMiteThroughput),
                    FormatLargeNumber(totalDsbUops),
                    FormatLargeNumber(totalMiteUops)};
                return results;
            }
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Per-Core ALU Port Utilization"; }
            public string[] columns = new string[] { "Item", "Core Instructions", "Core IPC", "Port 0", "Port 1", "Port 5" };

            public ALUPortUtilization(SandyBridge intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnablePerformanceCounters();

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // Counting per-core here, not per-thread, so set AnyThread bits for instructions/unhalted cycles
                    ulong fixedCounterConfigurationValue = 1 |        // enable FixedCtr0 for os (count kernel mode instructions retired)
                                                               1UL << 1 | // enable FixedCtr0 for usr (count user mode instructions retired)
                                                               1UL << 2 | // set AnyThread for FixedCtr0 (count instructions across both core threads)
                                                               1UL << 4 | // enable FixedCtr1 for os (count kernel mode unhalted thread cycles)
                                                               1UL << 5 | // enable FixedCtr1 for usr (count user mode unhalted thread cycles)
                                                               1UL << 6 | // set AnyThread for FixedCtr1 (count core clocks not thread clocks)
                                                               1UL << 8 | // enable FixedCtr2 for os (reference clocks in kernel mode)
                                                               1UL << 9;  // enable FixedCtr2 for usr (reference clocks in user mode)
                    Ring0.WriteMsr(IA32_FIXED_CTR_CTRL, fixedCounterConfigurationValue, 1UL << threadIdx);

                    // Set PMC0 to cycles when uops are executed on port 0
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xA1, 0x01, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: true, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count ^ for port 1
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xA1, 0x02, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count ^ for port 5
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xA1, 0x80, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalP0Uops = 0;
                ulong totalP1Uops = 0;
                ulong totalP5Uops = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong p0Uops, p1Uops, p5Uops;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out p0Uops);
                    Ring0.ReadMsr(IA32_A_PMC1, out p1Uops);
                    Ring0.ReadMsr(IA32_A_PMC2, out p5Uops);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);

                    totalP0Uops += p0Uops;
                    totalP1Uops += p1Uops;
                    totalP5Uops += p5Uops;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float ipc = (float)retiredInstructions / activeCycles;
                    float p0Util = (float)p0Uops / activeCycles * 100;
                    float p1Util = (float)p1Uops / activeCycles * 100;
                    float p5Util = (float)p5Uops / activeCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", p0Util),
                        string.Format("{0:F2}%", p1Util),
                        string.Format("{0:F2}%", p5Util) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallP0Util = (float)totalP0Uops / totalActiveCycles * 100;
                float overallP1Util = (float)totalP1Uops / totalActiveCycles * 100;
                float overallP5Util = (float)totalP5Uops / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                    "N/A",
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallP0Util),
                    string.Format("{0:F2}%", overallP1Util),
                    string.Format("{0:F2}%", overallP5Util) };
                return results;
            }
        }
    }
}
