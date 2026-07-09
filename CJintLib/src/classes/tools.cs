using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Threading;
using Jint.Native;
using Newtonsoft.Json.Linq;

namespace CJintLib {
	public static class JsCallback {
		public static void callback(object data) {
			JsConsole.writeLine(ConsoleColor.Cyan, "CLBK", data);
		}
	}

	public class JsBase : CObject {
		public JsEngine Engine { get; set; }
	}

	public class JsConsole : JsBase {
		public static Stopwatch stopWatch = new Stopwatch();
		public void log(params object[] args) => writeLine(ConsoleColor.Gray, "LOG ", args);
		public void debug(params object[] args) => writeLine(ConsoleColor.DarkBlue, "DBG ", args);
		public void info(params object[] args) => writeLine(ConsoleColor.Blue, "INFO", args);
		public void warn(params object[] args) => writeLine(ConsoleColor.Magenta, "WARN", args);
		public void error(params object[] args) => writeLine(ConsoleColor.Red, "ERR ", args);

		public static void writeLine(ConsoleColor? color, string type, params object[] args) {
			if (!stopWatch.IsRunning)
				stopWatch.Start();

			if (color.HasValue)
				Console.ForegroundColor = color.Value;
			Console.Write($"[{stopWatch.Elapsed}]  [{Thread.CurrentThread.ManagedThreadId}]  [{type}]  ");
			if (color.HasValue)
				Console.ResetColor();

			foreach (var arg in args) {
				var v = arg;
				if (v is JsObject jo)
					v = jo.ToString();
				else if (v is ExpandoObject dyn)
					v = JToken.FromObject(v);

				Console.Write(v);
				Console.Write(' ');
			}
			Console.WriteLine();
		}
	}
}
