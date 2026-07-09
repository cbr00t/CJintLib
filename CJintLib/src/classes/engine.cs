using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Acornima.Ast;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Modules;

using Newtonsoft.Json.Linq;

namespace CJintLib {
	public class JsEngine : CObject, IDisposable {
		public delegate void UpdateEngineProc(JsEngine sender, Jint.Engine engine);

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
		public JsConsole Con { get; set; }
		public JsApp App { get; set; }
		public static JSCoreLib CoreLib { get; set; } = new JSCoreLib();
		public static JSBootCode BootLib { get; set; } = new JSBootCode();
		public CList<JsLib> Libs { get; set; } = new CList<JsLib>();
		public bool EventLoopRunning {
			get => ti_eventLoop?.IsAlive ?? false;
		}

		public event UpdateEngineProc PreUpdateEngine, PostUpdateEngine;

		#region Builders
		public static T From<T>(IEnumerable<string> names) where T : JsEngine, new() {
			var res = new T();
			if (names.bosDegilMi())
				res.addLibs(names);
			return res;
		}
		public static T From<T>(params string[] names) where T : JsEngine, new() =>
			From<T>(names as IEnumerable<string>);
		public static JsEngine From(IEnumerable<string> names) =>
			From<JsEngine>(names);
		public static JsEngine From(params string[] names) =>
			From<JsEngine>(names);
		#endregion

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

		#region Engine Init
		protected virtual JsEngine init() {
			Engine = new Engine(Args);
			Con = new JsConsole { Engine = this };
			App = new JsApp { Engine = this };
			this.preUpdateEngine()
				.fillLibs()
				.preBoot()
				.afterPostBoot()
				.postUpdateEngine();
			return this;
		}
		protected virtual JsEngine argsDuzenle(ref Jint.Options args) {
			args
				.EnableModules(new DefaultModuleLoader(JsLib.JintRoot, false))
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
		protected JsEngine preUpdateEngine() =>
			_updateEngine(PreUpdateEngine);
		protected JsEngine postUpdateEngine() =>
			_updateEngine(PreUpdateEngine);
		protected JsEngine _updateEngine(UpdateEngineProc handler) {
			handler?.Invoke(this, Engine);
			return this;
		}

		#region Internals
		protected JsEngine fillLibs() {
			if (!BootLib.fill())
				throw new Exception("JSEngine Boot code init failed");
			if (!CoreLib.fill())
				throw new Exception("JSEngine Core code init failed");
			if (Libs.bosDegilMi()) {
				foreach (var lib in Libs)
					lib.fill();
			}
			return this;
		}
		protected JsEngine beforePreBoot() {
			var eng = Engine;
			var g = eng.Global;
			eng
				.SetValue("g", g)
				.SetValue("global", g)
				.SetValue("window", g)
				.SetValue("self", g)
				.SetValue("app", App)
				.SetValue("importLibs", new JsApp.ImportLibsProc(App.importLibs));
			return this;
		}
		protected JsEngine preBoot() {
			beforePreBoot();
			evalSync(BootLib.Pre);
			afterPreBoot();
			return this;
		}
		protected JsEngine afterPreBoot() {
			var eng = Engine;
			eng.SetValue("callback", new Action<object>(JsCallback.callback));
			eng.SetValue("console", Con);
			return this;
		}
		protected JsEngine postBoot() {
			beforePostBoot();
			evalSync(BootLib.Post);
			return this;
		}
		protected JsEngine beforePostBoot() {
			var eng = Engine;
			eng.SetValue("delay", new Func<int, Task>(ms =>
				Task.Delay(ms)));
			return this;
		}
		protected JsEngine afterPostBoot() {
			evalSync(CoreLib);
			evalAsync(Libs);
			return this;
		}
		#endregion
		#endregion

		#region API: Eval
		public JsValue eval(IEnumerable<JsLib> libs, bool sync = true, int? timeout = null) {
			if (libs.bosMu())
				return null;

			JsValue res = null;
			foreach (var s in libs)
				res = eval(s, sync, timeout);

			return res;
		}
		public JsValue eval(JsLib lib, bool sync = true, int? timeout = null) {
			if (lib == null)
				return null;

			JsValue res = null;
			foreach (var s in lib)
				res = eval(s, sync, timeout);

			return res;
		}
		public JsValue eval(Prepared<Script> s, bool sync = false, int? timeout = null) {
			try {
				return (
					sync
						? Engine.Evaluate(s)
						: Engine.EvaluateAsync(s, CancellationToken.None)
							.SWait(timeout)
				);
			}
			catch (AggregateException ex) { throw ex.InnerException ?? ex; }
			catch (JavaScriptException ex) { throw ex.InnerException ?? ex; }
		}
		public JsValue eval(string s, bool sync = false, int? timeout = null) {
			if (s.bosMu())
				return null;

			s = fixEvalCode(s);
			try {
				return (
					sync
						? Engine.Evaluate(s, s)
						: Engine.EvaluateAsync(s, s, CancellationToken.None).
							SWait(timeout)
				); 
			}
			catch (AggregateException ex) { throw ex.InnerException ?? ex; }
			catch (JavaScriptException ex) { throw ex.InnerException ?? ex; }
		}

		public JsValue evalAsync(IEnumerable<JsLib> libs, int? timeout = null) =>
			eval(libs, false, timeout);
		public JsValue evalAsync(JsLib lib, int? timeout = null) =>
			eval(lib, false, timeout);
		public JsValue evalAsync(Prepared<Script> s, int? timeout = null) =>
			eval(s, false, timeout);
		public JsValue evalAsync(string s, int? timeout = null) =>
			eval(s, false, timeout);
		public JsValue evalSync(IEnumerable<JsLib> libs, int? timeout = null) =>

			eval(libs, true, timeout);
		public JsValue evalSync(JsLib lib, int? timeout = null) =>
			eval(lib, true, timeout);
		public JsValue evalSync(Prepared<Script> s, int? timeout = null) =>
			eval(s, true, timeout);
		public JsValue evalSync(string s, int? timeout = null) =>
			eval(s, true, timeout);

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
		public JsEngine stopEventLoop() {
			ti_eventLoop?.abort();
			task_eventLoop = null;
			return this;
		}
		public JsEngine eventLoop(int? timeout = null) {
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
		protected JsEngine processTasks() {
			Engine?.Advanced?.ProcessTasks();
			return this;
		}
		#endregion

		#region Tools
		public JsLib addLibs(IEnumerable<string> names) {
			if (names.bosMu())
				return null;

			var lib = JsLib.From(names);
			if (lib == null)
				return null;

			Libs.Add(lib);
			return lib;
		}
		public JsLib addLibs(params string[] names) =>
			addLibs((IEnumerable<string>)names);
		public JsEngine clearLibs() {
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
		#endregion
	}
}
