using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Acornima.Ast;
using Jint;
using Jint.Native;
using Jint.Runtime.Modules;

using Newtonsoft.Json.Linq;

namespace CJintLib {
	public class JSEngine : CObject, IDisposable {
		public delegate void UpdateEngineProc(JSEngine sender, Jint.Engine engine);

		public const string WWWRoot = @"/appserv/www/skyerp";
		public const string JintRoot = WWWRoot + @"/jint";

		protected Jint.Engine engine;
		protected Jint.Options args;
		protected CThreadInfo ti_eventLoop;
		protected Task task_eventLoop;
		// public static readonly ScriptParsingOptions ParseOpts = ScriptParsingOptions.Default;

		public Jint.Engine Engine {
			get {
				if (engine == null)
					init();
				return engine;
			}
			set =>
				engine = value;
		}
		public Jint.Options Args {
			get {
				if (args == null) {
					args = new Options();
					argsDuzenle(ref args);
				}
				return args;
			}
			set =>
				args = value;
		}
		public static JSCoreLib CoreLib { get; set; } = new JSCoreLib();
		public static JSBootCode BootLib { get; set; } = new JSBootCode();
		public CList<JSLib> Libs { get; set; } = new CList<JSLib>();
		public bool EventLoopRunning {
			get => ti_eventLoop?.IsAlive ?? false;
		}

		public event UpdateEngineProc UpdateEngine;

		public static T From<T>(IEnumerable<string> names) where T : JSEngine, new() {
			var res = new T();
			if (names.bosDegilMi())
				res.addLibs(names);
			return res;
		}
		public static T From<T>(params string[] names) where T : JSEngine, new() =>
			From<T>(names as IEnumerable<string>);
		public static JSEngine From(IEnumerable<string> names) =>
			From<JSEngine>(names);
		public static JSEngine From(params string[] names) =>
			From<JSEngine>(names);

		protected virtual JSEngine init() {
			var eng = Engine = new Engine(Args);
			updateEngine();

			var boot = BootLib;
			var core = CoreLib;
			if (!boot.fill())
				throw new Exception("JSEngine Boot code init failed");
			if (!core.fill())
				throw new Exception("JSEngine Core code init failed");

			if (Libs.bosDegilMi()) {
				foreach (var lib in Libs)
					lib.fill();
			}
			eval(boot.Pre, true);
			eng.SetValue("callback", new Action<object>(JsCallback.callback));
			eng.SetValue("console", new JsConsole());
			eval(core, true);
			eng.SetValue("delay", new Func<int, Task>(ms =>
				Task.Delay(ms)));
			eval(boot.Post, true);
			eval(Libs, false);

			return this;
		}

		public virtual void Dispose() {
			stopEventLoop();
			CoreLib?.Dispose();
			BootLib?.Dispose();
			if (Libs.bosDegilMi()) {
				foreach (var lib in Libs)
					lib.Dispose();
				clearLibs();
				Libs = null;
			}
			args = null;
			engine?.Dispose();
			engine = null;
		}
		protected virtual JSEngine argsDuzenle(ref Jint.Options args) {
			args
				.EnableModules(new DefaultModuleLoader(WWWRoot))
				.Strict(true)
				.AllowClr()
				.AllowClrWrite(true)
				.CatchClrExceptions(ex => true)
				.AllowOperatorOverloading(true)
				.PreferJsPrototypeMethods(true)
				.DebugMode(false)
				.DebuggerStatementHandling(Jint.Runtime.Debugger.DebuggerStatementHandling.Clr)
				.InitialStepMode(Jint.Runtime.Debugger.StepMode.Into);
			var m = args.Modules;
			m.RegisterRequire = true;
			args.Host.StringCompilationAllowed = true;
			return this;
		}
		protected virtual JSEngine updateEngine() {
			if (UpdateEngine != null) {
				var t = Engine;
				UpdateEngine(this, t);
			}
			return this;
		}

		public JsValue eval(IEnumerable<JSLib> libs, bool sync = true, int? timeout = null) {
			if (libs.bosMu())
				return null;

			JsValue res = null;
			foreach (var s in libs)
				res = eval(s, sync, timeout);

			return res;
		}
		public JsValue eval(JSLib lib, bool sync = true, int? timeout = null) {
			if (lib == null)
				return null;

			JsValue res = null;
			foreach (var s in lib)
				res = eval(s, sync, timeout);

			return res;
		}
		public JsValue eval(Prepared<Script> s, bool sync = false, int? timeout = null) {
			return (
				sync
					? Engine.Evaluate(s)
					: Engine.EvaluateAsync(s, CancellationToken.None)
						.SWait(timeout)
			);
		}
		public JsValue eval(string s, bool sync = false, int? timeout = null) {
			if (s.bosMu())
				return null;

			s = fixEvalCode(s);
			return (
				sync
					? Engine.Evaluate(s, s)
					: Engine.EvaluateAsync(s, s, CancellationToken.None).
						SWait(timeout)
			);
		}

		public Task startEventLoop(int? timeout = null) {
			if (!EventLoopRunning) {
				ti_eventLoop = null;
				task_eventLoop = null;
			}

			if (task_eventLoop != null)
				return task_eventLoop;

			task_eventLoop = Task.Factory.StartNew(() => {
				ti_eventLoop = CThreadInfo.CurrentThreadInfo;
				try { eventLoop(timeout); }
				catch (ThreadAbortException) {
					try { Thread.ResetAbort(); }
					catch (Exception) { }
					return;
				}
				catch (ThreadInterruptedException) { return; }
				finally { ti_eventLoop = null; task_eventLoop = null; }
			}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
			for (var i = 0; ti_eventLoop == null && i < 10; i++) {
				10.millisecondsWait();
				Application.DoEvents();
			}

			return task_eventLoop;
		}
		public JSEngine stopEventLoop() {
			ti_eventLoop?.abort();
			task_eventLoop = null;
			return this;
		}
		public JSEngine eventLoop(int? timeout = null) {
			const int InternalDelayMS = 10;
			timeout = timeout ?? Timeout.Infinite;
			var w = timeout < 0 ? null : Stopwatch.StartNew();
			while (!(Engine == null || CThreadInfo.isAbortRequestedForCurrentThread())) {
				processTasks();
				if (w != null && w.ElapsedMilliseconds >= timeout.Value)
					break;
				InternalDelayMS.millisecondsWait();
				Application.DoEvents();
			}
			return this;
		}
		public JSEngine processTasks() {
			Engine?.Advanced?.ProcessTasks();
			return this;
		}

		public JSLib addLibs(IEnumerable<string> names) {
			if (names.bosMu())
				return null;

			var lib = JSLib.From(names);
			if (lib == null)
				return null;

			Libs.Add(lib);
			return lib;
		}
		public JSLib addLibs(params string[] names) =>
			addLibs((IEnumerable<string>)names);
		public JSEngine clearLibs() {
			Libs?.Clear();
			return this;
		}

		public static string fixEvalCode(string s) {
			/*if (s.bosMu())
				return s;
			if (!(s.StartsWith("(async") || s.StartsWith("(async function(")))
				s = $"(async (e = {{}}) => {{\n	{s}\n}})()";*/
			return s;
		}
	}
}
