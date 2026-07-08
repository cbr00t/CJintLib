using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Acornima.Ast;
using Jint;

namespace CJintLib {
	public class JSLib : Cache, IEnumerable<Prepared<Script>> {
		public const string WWWRoot = @"/appserv/www/skyerp";
		public const string JintRoot = WWWRoot + @"/jint";
		public const string CF_Config = @"config.php";
		protected Prepared<Script>? script;

		public CList<string> Libs { get; set; } = new CList<string>();
		public IEnumerable<string> LibFiles {
			get => Libs.Select(n =>
				Path.Combine(WWWRoot, $"{n}.js"));
		}
		public Prepared<Script>? Script {
			get => script;
			set => script = value;
		}
		public override bool NeedsUpdate {
			get => (
				base.NeedsUpdate ||
				LibFiles.Any(_f => {
					var f = _f.asFileInfo();
					return f.Exists && f.LastWriteTime > CacheTS;
				})
			);
		}

		public JSLib() : base() { }
		public override void Dispose() {
			base.Dispose();
			script = null;
			Libs?.Clear();
			Libs = null;
		}
		public override bool fill() =>
			base.fill() && fetchLibs(out script, Libs);
		public static bool fetchLibs(out Prepared<Script>? t, IEnumerable<string> names) {
			t = null;
			if (names.bosMu())
				return true;

			var files = new CList<string>();
			foreach (var _n in names.Where(_ => _.bosDegilMi())) {
				var n = _n;
				if (!Path.IsPathRooted(n))
					n = WWWRoot.pathCombine(n);
				if ((Path.GetExtension(n)?.Length ?? 0) < 2)
					n += ".js";
				n = n.Replace('\\', '/');
				files.Add(n);
			}

			var sb = new StringBuilder();
			foreach (var f in files) {
				string d = null;
				try { d = File.ReadAllText(f); }
				catch (IOException ex) { }
				if (d.bosMu())
					continue;

				sb.AppendLine($"\n//# sourceURL=[{f.relativePathFor(WWWRoot)}\n");
				d = JSEngine.fixEvalCode(d);
				sb.AppendLine(d);
				sb.AppendLine();
			}
			if (files.bosMu())
				return true;

			var code = sb.ToString();
			if (code.bosMu())
				return true;

			var res = Engine.PrepareScript(code, code, true);
			t = res;

			return res.IsValid;
		}
		public static bool fetchLibs(out Prepared<Script>? t, params string[] names) =>
			fetchLibs(out t, names as IEnumerable<string>);

		public static T From<T>(IEnumerable<string> names) where T : JSLib, new() {
			var res = new T();
			if (names.bosMu())
				return default;
			res.add(names);
			return res;
		}
		public static T From<T>(params string[] names) where T : JSLib, new() =>
			From<T>(names as IEnumerable<string>);
		public static JSLib From(IEnumerable<string> names) =>
			From<JSLib>(names);
		public static JSLib From(params string[] names) =>
			From<JSLib>(names);

		public JSLib add(IEnumerable<string> names) {
			if (names.bosDegilMi())
				Libs.AddRange(names);
			return this;
		}
		public JSLib add(params string[] names) =>
			add((IEnumerable<string>)names);
		public JSLib clear() {
			Libs?.Clear();
			return this;
		}

		public IEnumerator<Prepared<Script>> getEnumerator() {
			var s = Script;
			if (s.HasValue)
				yield return s.Value;
		}
		IEnumerator IEnumerable.GetEnumerator() =>
			getEnumerator();
		IEnumerator<Prepared<Script>> IEnumerable<Prepared<Script>>.GetEnumerator() =>
			getEnumerator();
	}

	public class JSCoreLib : JSLib {
		public static readonly string[] StaticLibs = new[] {
			"lib/etc/localization.js",
			"lib/ortak/utils.js",
			"classes/ortak/CObject.js",
			"classes/ortak/basitSiniflar.js",
			"classes/ortak/CKodVeAdi.js",
			"lib/session/roller.js",
			"lib/session/session.js"
		};

		public JSCoreLib() : base() =>
			add(StaticLibs);
	}

	public class JSBootCode : Cache {
		public const string
			CF_Pre = @"jint/lib/preboot",
			CF_Post = @"jint/lib/postboot";

		public JSLib Pre { get; set; } = JSLib.From(CF_Pre);
		public JSLib Post { get; set; } = JSLib.From(CF_Post);

		public override void Dispose() {
			base.Dispose();
			Pre?.Dispose(); Post?.Dispose();
			Pre = Post = null;
		}
		public override bool fill() =>
			base.fill() && Pre.fill() && Post.fill();
	}
}
