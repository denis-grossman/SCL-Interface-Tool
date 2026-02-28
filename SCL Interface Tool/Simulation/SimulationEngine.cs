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
        public long CycleTimeTicks { get; private set; } // High-resolution ticks
        public event Action<Exception> OnError;
        public void StepScans(int count)
        {
            if (_runner == null) return;

            lock (MemoryLock)
            {
                for (int i = 0; i < count; i++)
                {
                    _context.PrepareForNextScanCycle();
                    _runner(_globals).Wait();
                    CycleTimeTicks++; // Just incrementing a dummy counter for stat tracking during tests
                }
            }
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

                CycleTimeTicks = sw.ElapsedTicks; // High-resolution
                CycleTimeMs = (int)sw.ElapsedMilliseconds;

                try { await Task.Delay(Math.Max(1, 10 - CycleTimeMs), token); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
