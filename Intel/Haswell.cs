﻿using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class Haswell : ModernIntelCpu
    {
        public Haswell()
        {
            monitoringConfigs = new MonitoringConfig[20];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new OpCachePerformance(this);
            monitoringConfigs[2] = new OpDelivery(this);
            monitoringConfigs[3] = new ALUPortUtilization(this);
            monitoringConfigs[4] = new LSPortUtilization(this);
            monitoringConfigs[5] = new LoadDtlbConfig(this);
            monitoringConfigs[6] = new MoveElimConfig(this);
            monitoringConfigs[7] = new DispatchStalls(this);
            monitoringConfigs[8] = new LoadDataSources(this);
            monitoringConfigs[9] = new L2Cache(this);
            monitoringConfigs[10] = new L1DFill(this);
            monitoringConfigs[11] = new Rename(this);
            monitoringConfigs[12] = new IFetch(this);
            monitoringConfigs[13] = new MemBound(this);
            monitoringConfigs[14] = new ArchitecturalCounters(this);
            monitoringConfigs[15] = new RetireSlots(this);
            monitoringConfigs[16] = new DecodeHistogram(this);
            monitoringConfigs[17] = new OCHistogram(this);
            monitoringConfigs[18] = new OffcoreBw(this);
            monitoringConfigs[19] = new OffcoreReqs(this);
            architectureName = "Haswell";
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "ALU Port Util/Pwr"; }

            public ALUPortUtilization(Haswell intelCpu)
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
                    // Set PMC0 to cycles when uops are executed on port 0
                    // anyThread sometimes works (i7-4712HQ) and sometimes not (E5-1620v3). It works on SNB.
                    // don't set anythread for consistent behavior
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xA1, 0x01, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0));

                    // Set PMC1 to count ^ for port 1
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xA1, 0x02, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count ^ for port 5
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA1, 0x20, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count ^ for port 6
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xA1, 0x40, true, true, false, false, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx], false);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                results.overallCounterValues = cpu.GetOverallCounterValues("Port 0", "Port 1", "Port 2", "Port 3");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "PP0 Pwr", "Instr/Watt", "Cores Instr/Watt", "Port 0", "Port 1", "Port 5", "Port 6" };

            public string GetHelpText()
            {
                return "Port 0 - ALU, FMUL/FMA, predicted not-taken branches\n" +
                    "Port 1 - ALU, IMUL, FMUL/FADD/FMA\n" +
                    "Port 5 - ALU, integer vector, crypto\n" +
                    "Port 6 - ALU, predicted taken branches";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool overall)
            {
                float ipc = counterData.instr / counterData.activeCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.activeCycles),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", ipc),
                    overall ? string.Format("{0:F2} W", counterData.packagePower) : "N/A",
                    overall ? string.Format("{0:F2} W", counterData.pp0Power) : "N/A",
                    overall ? FormatLargeNumber(counterData.instr / counterData.packagePower) : "N/A",
                    overall ? FormatLargeNumber(counterData.instr / counterData.pp0Power) : "N/A",
                    string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc1 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc2 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles) };
            }
        }

        public class LSPortUtilization : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "AGU/LS Port Utilization"; }

            public LSPortUtilization(Haswell intelCpu)
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
                    // Set PMC0 to cycles when uops are executed on port 2
                    ulong p2Ops = GetPerfEvtSelRegisterValue(0xA1, 0x04, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, p2Ops);

                    // Set PMC1 to count ^ for port 3
                    ulong p3Ops = GetPerfEvtSelRegisterValue(0xA1, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, p3Ops);

                    // Set PMC2 to count ^ for port 4
                    ulong p4Ops = GetPerfEvtSelRegisterValue(0xA1, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, p4Ops);

                    // Set PMC3 to count ^ for port 7
                    ulong p7Ops = GetPerfEvtSelRegisterValue(0xA1, 0x80, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, p7Ops);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "P2 AGU", "P3 AGU", "P4 StoreData", "P7 StoreAGU" };

            public string GetHelpText()
            {
                return "Port 2/3 - Load/Store address generation\n" +
                    "Port 4 - Store Data. Each store sends an op to port 4, and one to port 2 or 3 or 7\n" +
                    "Port 7 - Store address generation. Only handles simple address calculations (no index reg)\n";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.instr / counterData.activeCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.activeCycles),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc1 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc2 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles) };
            }
        }

        public class LoadDtlbConfig : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "DTLB (loads)"; }

            public LoadDtlbConfig(Haswell intelCpu)
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
                    // Set PMC0 to count page walk duration
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x08, 0x10, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count DTLB miss -> STLB hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x08, 0x60, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to DTLB misses that cause walks
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x08, 0x1, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count completed walks
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x08, 0xE, true, true, false, false, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "DTLB Miss STLB Hit", "DTLB Miss, Page Walk", "Page Walk Completed", "DTLB MPKI", "STLB Hitrate", "Page Walk Duration", "Page Walk Cycles", "% Walks Completed" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.instr / counterData.activeCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.activeCycles),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", ipc),
                    FormatLargeNumber(counterData.pmc1),
                    FormatLargeNumber(counterData.pmc2),
                    FormatLargeNumber(counterData.pmc3),
                    string.Format("{0:F2}", (counterData.pmc1 + counterData.pmc2) / counterData.instr),
                    string.Format("{0:F2}%", 100 * counterData.pmc1 / (counterData.pmc1 + counterData.pmc2)),
                    string.Format("{0:F2} clks", counterData.pmc0 / counterData.pmc2),
                    string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                    string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.pmc2) };
            }
        }

        public class MoveElimConfig : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Move Elimination"; }

            public MoveElimConfig(Haswell intelCpu)
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
                    // Set PMC0 to count eliminated integer move elim candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x58, 0x1, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count eliminated simd move elim candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x58, 0x2, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count not eliminated int move candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x58, 0x4, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count not eliminated simd move candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x58, 0x8, true, true, false, false, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "% movs eliminated", "% int movs elim", "% simd movs elim", "eliminated int movs", "int elim candidates", "eliminated simd movs", "simd elim candidates" };

            public string GetHelpText()
            {
                return "Eliminated movs have zero latency and don't use an execution port, but still use up frontend/renamer bandwidth and backend tracking resources";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.instr / counterData.activeCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.activeCycles),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * (counterData.pmc0 + counterData.pmc1) / (counterData.pmc0 + counterData.pmc1 + counterData.pmc2 + counterData.pmc3)),
                    string.Format("{0:F2}%", 100 * counterData.pmc0 / (counterData.pmc0 + counterData.pmc2)),
                    string.Format("{0:F2}%", 100 * counterData.pmc1 / (counterData.pmc1 + counterData.pmc3)),
                    FormatLargeNumber(counterData.pmc0),
                    FormatLargeNumber(counterData.pmc0 + counterData.pmc2),
                    FormatLargeNumber(counterData.pmc1),
                    FormatLargeNumber(counterData.pmc1 + counterData.pmc3)
                };
            }
        }

        public class DispatchStalls : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }

            public DispatchStalls(Haswell intelCpu)
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
                    // Set PMC0 to count stalls because the load buffer's full
                    ulong lbFull = GetPerfEvtSelRegisterValue(0xA2, 0x02, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, lbFull);

                    // Set PMC1 ^^ SB full
                    ulong sbFull = GetPerfEvtSelRegisterValue(0xA2, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, sbFull);

                    // Set PMC2 ^^ RS full
                    ulong rsFull = GetPerfEvtSelRegisterValue(0xA2, 0x04, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, rsFull);

                    // Set PMC3 ^^ ROB full
                    ulong robFull = GetPerfEvtSelRegisterValue(0xA2, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, robFull);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "(LDQ Full?)", "STQ Full", "RS Full", "ROB Full" };

            public string GetHelpText()
            {
                return "Load queue full umask is undocumented in Haswell";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", counterData.pmc0 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc1 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc2 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc3 / counterData.activeCycles * 100)};
            }
        }

        public class L2Cache : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Cache(Haswell intelCpu)
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
                    // Set PMC0 to count l2 references
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x24, 0xFF, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count l2 misses
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x24, 0x3F, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count L2 lines in
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xF1, 0x7, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count dirty L2 lines evicted
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xF2, 0x6, true, true, false, false, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 References", "L2 Misses", "L2 Lines In", "L2 Dirty Lines Evicted");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "Pkg Pwr", "Instr/Watt", "IPC", "L2 Hitrate", "L2 Hit BW", "L2 Fill BW", "L2 Writeback BW" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc0 - counterData.pmc1) / counterData.pmc0),
                        FormatLargeNumber((counterData.pmc0 - counterData.pmc1) * 64) + "B",
                        FormatLargeNumber(counterData.pmc2 * 64) + "B",
                        FormatLargeNumber(counterData.pmc3 * 64) + "B",
                };
            }
        }

        public class LoadDataSources : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Load Data Sources"; }

            public LoadDataSources(Haswell intelCpu)
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
                    // PMC0 - all retired loads
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xD0, 0x82, true, true, false, false, false, false, true, false, 0));

                    // PMC1 - L1 miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xD1, 0x8, true, true, false, false, false, false, true, false, 0));

                    // PMC2 - L2 miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xD1, 0x10, true, true, false, false, false, false, true, false, 0));

                    // PMC3 - L3 miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xD1, 0x20, true, true, false, false, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Ret. Loads", "L1/LFB %", "L1 MPKI", "L2 %", "L2 MPKI", "L3 %", "L3 MPKI", "DRAM %" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float l1LfbHit = counterData.pmc0 - counterData.pmc1;
                float l2Hit = counterData.pmc1 - counterData.pmc2;
                float l3hit = counterData.pmc2 - counterData.pmc3;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        FormatLargeNumber(counterData.pmc0),
                        string.Format("{0:F2}%", 100 * l1LfbHit / counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}%", 100 * l2Hit / counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}%", 100 * l3hit / counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.pmc0),
                };
            }
        }

        public class Rename : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Rename"; }

            public Rename(Haswell intelCpu)
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
                    // PMC0 - all uops issued across both threads, cmask 1
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xE, 0x1, true, true, false, false, false, anyThread: true, true, false, cmask: 1));
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xE, 0x1, true, true, false, false, false, anyThread: true, true, false, cmask: 2));
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xE, 0x1, true, true, false, false, false, anyThread: true, true, false, cmask: 3));
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xE, 0x1, true, true, false, false, false, anyThread: true, true, false, cmask: 4));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("uops issued cmask 1", "uops issued cmask 2", "uops issued cmask 3", "uops issued cmask 4");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "uops issued", "uops issued/c", "1 uop", "2 uops", "3 uops", "4 uops", "issue active" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float oneOp = counterData.pmc0 - counterData.pmc1;
                float twoOps = counterData.pmc1 - counterData.pmc2;
                float threeOps = counterData.pmc2 - counterData.pmc3;
                float opsIssued = oneOp + 2 * twoOps + 3 * threeOps + 4 * counterData.pmc3;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        FormatLargeNumber(opsIssued),
                        string.Format("{0:F2}", opsIssued / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * oneOp / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * twoOps / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * threeOps / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                };
            }
        }

        public class IFetch : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Instruction Fetch"; }

            public IFetch(Haswell intelCpu)
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
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x80, 0x1, true, true, false, false, false, false, true, false, 0)); // ic hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x80, 0x2, true, true, false, false, false, false, true, false, 0)); // ic miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x80, 0x4, true, true, false, false, false, false, true, false, 0)); // ifetch stall
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x87, 0x4, true, true, false, false, false, false, true, false, 0)); // iq full
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Hit", "IC Miss", "IFetch Stall Cycles", "IQ Full Stall Cycles");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", "IC Hitrate", "IC MPKI", "IC Hits", "IFetch Stall", "IQ Full Stall" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc0, counterData.pmc0 + counterData.pmc1),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        FormatLargeNumber(counterData.pmc0),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles)
                };
            }
        }

        public class MemBound : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Memory Bound"; }

            public MemBound(Haswell intelCpu)
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
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xA3, 0x4, true, true, false, false, false, false, true, false, cmask: 4)); // no execute
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xA3, 0x6, true, true, false, false, false, false, true, false, cmask: 6)); // LDM pending
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA3, 0xC, true, true, false, false, false, false, true, false, cmask: 0xC)); // L1D pending, pmc2 only
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xA3, 0x5, true, true, false, false, false, false, true, false, cmask: 5)); // L2 Pending
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("No execute cycles", "LDM Pending Cycles", "Stall L1D Miss Pending Cycles", "Stall L2 Miss Pending cycles");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", "No Execute", "Stall LDM Pending", "Stall, L1D Miss", "(Stall, L2 Miss)" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc0, counterData.activeCycles),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles)
                };
            }
        }

        public class RetireSlots : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Retire BW"; }

            public RetireSlots(Haswell intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                // up to 4 retire slots used each cycle
                cpu.ProgramPerfCounters(GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, cmask: 1),
                    GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, cmask: 2),
                    GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, cmask: 3),
                    GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, cmask: 4));
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Retire slots cmask 1", "Retire slots cmask 2", "Retire slots cmask 3", "Retire slots cmask 4");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", "Retire Slots Used", "Retire Slots/Instr", "Retire Slots/Clk", "Retire Active", "1 Slot", "2 Slots", "3 Slots", "4 Slots" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float retireSlotsUsed = (counterData.pmc0 - counterData.pmc1) + 
                    2 * (counterData.pmc1 - counterData.pmc2) + 
                    3 * (counterData.pmc2 - counterData.pmc3) + 
                    4 * counterData.pmc3;

                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatLargeNumber(retireSlotsUsed),
                        string.Format("{0:F2}", retireSlotsUsed / counterData.instr),
                        string.Format("{0:F2}", retireSlotsUsed / counterData.activeCycles),
                        FormatPercentage(counterData.pmc0, counterData.activeCycles), // retire active - at least 1 slot used
                        FormatPercentage(counterData.pmc0 - counterData.pmc1, counterData.activeCycles), // 1 slot
                        FormatPercentage(counterData.pmc1 - counterData.pmc2, counterData.activeCycles), // 2 slots
                        FormatPercentage(counterData.pmc2 - counterData.pmc3, counterData.activeCycles), // 3 slots
                        FormatPercentage(counterData.pmc3, counterData.activeCycles) // 4 slots
                };
            }
        }

        public class OffcoreBw : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Offcore BW (Burst)"; }

            public OffcoreBw(ModernIntelCpu intelCpu)
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
                    // cmask 4
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 4));

                    // cmask 8
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 8));

                    // cmask 12
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 12));

                    // cmask 16
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 16));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx], false);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                results.overallCounterValues = cpu.GetOverallCounterValues("offcore req cmask 4", "offcore req cmask 8", "offcore req cmask 12", "offcore req cmask 16");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "offcore req cmask 4", "offcore req cmask 8", "offcore req cmask 12", "offcore req cmask 16" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool overall)
            {
                float oneOp = counterData.pmc0 - counterData.pmc1;
                float twoOps = counterData.pmc1 - counterData.pmc2;
                float threeOps = counterData.pmc2 - counterData.pmc3;
                float opCacheOps = oneOp + 2 * twoOps + 3 * threeOps + 4 * counterData.pmc3;
                return new string[] { label,
                       FormatLargeNumber(counterData.activeCycles),
                       FormatLargeNumber(counterData.instr),
                       string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                       overall ? string.Format("{0:F2} W", counterData.packagePower) : "N/A",
                       overall ? FormatLargeNumber(counterData.instr / counterData.packagePower) : "N/A",
                       FormatPercentage(counterData.pmc0, counterData.activeCycles),
                       FormatPercentage(counterData.pmc1, counterData.activeCycles),
                       FormatPercentage(counterData.pmc2, counterData.activeCycles),
                       FormatPercentage(counterData.pmc3, counterData.activeCycles),
                };
            }
        }

        public class OffcoreReqs : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Offcore Reqs"; }

            public OffcoreReqs(ModernIntelCpu intelCpu)
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
                    // SQ occupancy,  code/data
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 0));

                    // SQ requests
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xB0, 0x8 | 0x2, true, true, false, false, false, false, true, false, cmask: 0));

                    // SQ Full
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xB2, 0x1, true, true, false, false, false, false, true, false, 0));

                    // SQ occupancy cmask 16
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x60, 0x8 | 0x2, true, true, false, false, false, false, true, false, 16));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx], false);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                results.overallCounterValues = cpu.GetOverallCounterValues("Offcore Req Occupancy", "Offcore Requests", "SQ Full", "offcore req cmask 16");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "Offcore Requests * 64B", "Offcore req latency", "SQ Full", "SQ cmask 16" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool overall)
            {
                float oneOp = counterData.pmc0 - counterData.pmc1;
                float twoOps = counterData.pmc1 - counterData.pmc2;
                float threeOps = counterData.pmc2 - counterData.pmc3;
                float opCacheOps = oneOp + 2 * twoOps + 3 * threeOps + 4 * counterData.pmc3;
                return new string[] { label,
                       FormatLargeNumber(counterData.activeCycles),
                       FormatLargeNumber(counterData.instr),
                       string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                       overall ? string.Format("{0:F2} W", counterData.packagePower) : "N/A",
                       overall ? FormatLargeNumber(counterData.instr / counterData.packagePower) : "N/A",
                       FormatLargeNumber(64 * counterData.pmc1) + "B/s",
                       string.Format("{0:F2} clk", counterData.pmc0 / counterData.pmc1),
                       FormatPercentage(counterData.pmc2, counterData.activeCycles),
                       FormatPercentage(counterData.pmc3, counterData.activeCycles),
                };
            }
        }
    }
}
