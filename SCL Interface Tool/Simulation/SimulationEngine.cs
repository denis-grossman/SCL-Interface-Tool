using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using ExecutionContext = SCL_Interface_Tool.Simulation.ExecutionContext;

namespace SCL_Interface_Tool.Simulation
{
    public class SimulationEngine
    {
        public readonly object MemoryLock = new object();
        private ExecutionContext _context;
        private ScriptRunner<object> _runner;
        private SimulationGlobals _globals;
        private CancellationTokenSource _cts;

        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }
        public int CycleTimeMs { get; private set; }
        public long CycleTimeTicks { get; private set; }
        public event Action<Exception> OnError;

        /// <summary>
        /// Executes a fixed number of scan cycles. Used by "RUN X SCANS".
        /// When virtual time mode is active (i.e., during a test session),
        /// each scan advances the virtual clock by scanIntervalMs to keep
        /// timers progressing realistically.
        /// </summary>
        public void StepScans(int count, int scanIntervalMs = 10)
        {
            if (_runner == null) return;

            lock (MemoryLock)
            {
                for (int i = 0; i < count; i++)
                {
                    // If virtual time is active, advance clock per scan
                    if (SclStandardLib.UseVirtualTime)
                    {
                        if (SclStandardLib.VirtualTickCount == 0)
                        {
                            SclStandardLib.VirtualTickCount = Environment.TickCount64;
                        }
                        SclStandardLib.VirtualTickCount += scanIntervalMs;
                    }

                    _context.PrepareForNextScanCycle();
                    _runner(_globals).Wait();
                    CycleTimeTicks++;
                }
            }
        }


        /// <summary>
        /// Advances virtual time by the specified number of milliseconds,
        /// executing scan cycles at a realistic interval (e.g., every 10ms).
        /// Used by "RUN X MS". This is the critical fix for TON/TOF/TP timers.
        /// </summary>
        /// <param name="milliseconds">Total wall-clock time to simulate.</param>
        /// <param name="scanIntervalMs">Simulated scan interval in ms (default 10ms, typical S7-1500).</param>
        public void StepTime(int milliseconds, int scanIntervalMs = 10)
        {
            if (_runner == null) return;

            // Enable virtual time so TON/TOF/TP use our controlled clock
            SclStandardLib.UseVirtualTime = true;

            // Initialize virtual clock to current real time if starting fresh
            if (SclStandardLib.VirtualTickCount == 0)
            {
                SclStandardLib.VirtualTickCount = Environment.TickCount64;
            }

            int totalScans = Math.Max(1, milliseconds / scanIntervalMs);

            lock (MemoryLock)
            {
                for (int i = 0; i < totalScans; i++)
                {
                    // Advance virtual clock by one scan interval BEFORE executing the scan
                    SclStandardLib.VirtualTickCount += scanIntervalMs;

                    _context.PrepareForNextScanCycle();
                    _runner(_globals).Wait();
                    CycleTimeTicks++;
                }
            }

            // NOTE: We intentionally leave UseVirtualTime = true for the rest of the test session.
            // This ensures subsequent "RUN 1 SCANS" commands also see consistent time progression.
        }

        /// <summary>
        /// Call this when starting a new test session to reset the virtual clock.
        /// </summary>
        public void ResetVirtualTime()
        {
            SclStandardLib.UseVirtualTime = false;
            SclStandardLib.VirtualTickCount = 0;
        }


        public void Compile(string code, ExecutionContext context)
        {
            _context = context;
            _globals = new SimulationGlobals { Memory = _context.Memory };
            _runner = null;

            try
            {
                var options = ScriptOptions.Default
                    .WithReferences(Assembly.GetExecutingAssembly())
                    .WithImports("System", "System.Collections.Generic", "SCL_Interface_Tool.Simulation");
                var script = CSharpScript.Create(code, options, globalsType: typeof(SimulationGlobals));
                script.Compile();
                _runner = script.CreateDelegate();
            }
            catch (CompilationErrorException ex)
            {
                string errors = string.Join("\n", ex.Diagnostics);
                throw new Exception($"Compilation Syntax Error:\n{errors}\n\n--- Generated C# Code ---\n{code}");
            }
        }

        public void Start()
        {
            if (_runner == null) return;
            if (IsRunning && !IsPaused) return;
            if (IsPaused) { IsPaused = false; return; }

            IsRunning = true;
            IsPaused = false;
            _cts = new CancellationTokenSource();
            Task.Run(() => Ob1ScanLoop(_cts.Token));
        }

        public void Pause() => IsPaused = true;

        public void Stop()
        {
            IsRunning = false;
            IsPaused = false;
            _cts?.Cancel();
        }

        private async Task Ob1ScanLoop(CancellationToken token)
        {
            Stopwatch sw = new Stopwatch();
            while (!token.IsCancellationRequested)
            {
                if (IsPaused) { await Task.Delay(50, token); continue; }

                sw.Restart();
                try
                {
                    lock (MemoryLock)
                    {
                        _context.PrepareForNextScanCycle();
                        _runner(_globals).Wait(token);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Stop();
                    OnError?.Invoke(ex.InnerException ?? ex);
                    break;
                }
                sw.Stop();

                CycleTimeTicks = sw.ElapsedTicks;
                CycleTimeMs = (int)sw.ElapsedMilliseconds;

                try { await Task.Delay(Math.Max(1, 10 - CycleTimeMs), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
