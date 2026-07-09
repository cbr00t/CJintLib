using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

using Acornima.Ast;
using Jint;
using Jint.Native;

namespace CJintLib {
	public class JsLib : Cache, IEnumerable<Prepared<Script>> {
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

		public JsLib() : base() { }
		public override void Dispose() {
			base.Dispose();
			script = null;
			Libs?.Clear();
			Libs = null;
		}
		public override bool fill(string rootDir = null) =>
			base.fill(rootDir) && fetchLibs(out script, Libs, rootDir);
		public static bool fetchLibs(out Prepared<Script>? t, IEnumerable<string> names, string rootDir = null) {
			t = null;
			if (names.bosMu())
				return true;

			rootDir = rootDir ?? WWWRoot;
			var files = new CList<string>();
			foreach (var _n in names.Where(_ => _.bosDegilMi())) {
				var n = _n;
				if (!Path.IsPathRooted(n))
					n = rootDir.pathCombine(n);
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

				sb.AppendLine($"\n//# sourceURL=[{f.relativePathFor(rootDir)}\n");
				d = JsEngine.fixEvalCode(d);
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

		public static T From<T>(IEnumerable<string> names) where T : JsLib, new() {
			var res = new T();
			if (names.bosMu())
				return default;
			res.add(names);
			return res;
		}
		public static T From<T>(params string[] names) where T : JsLib, new() =>
			From<T>(names as IEnumerable<string>);
		public static JsLib From(IEnumerable<string> names) =>
			From<JsLib>(names);
		public static JsLib From(params string[] names) =>
			From<JsLib>(names);

		public JsLib add(IEnumerable<string> names) {
			if (names.bosDegilMi())
				Libs.AddRange(names);
			return this;
		}
		public JsLib add(params string[] names) =>
			add((IEnumerable<string>)names);
		public JsLib clear() {
			Libs?.Clear();
			return this;
		}

		public JsValue eval(JsEngine je, bool sync = false, int? timeout = null) =>
			je?.eval(this, sync, timeout);
		public JsValue evalAsync(JsEngine je, int? timeout = null) =>
			je?.eval(this, false, timeout);
		public JsValue evalSync(JsEngine je, int? timeout = null) =>
			je?.eval(this, true, timeout);

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

	public class JSCoreLib : JsLib {
		public static readonly string[] StaticLibs = new[] {
			"lib/etc/localization.js",
			"lib/ortak/utils.js",
			"classes/ortak/CObject.js",
			"classes/ortak/basitSiniflar.js",
			"classes/cIO/cIO.js",
			"classes/cIO/pInst.js",
			"classes/ortak/CKodVeAdi.js",
			"classes/ortak/CIdVeAdi.js",
			"classes/ortak/CDetayli.js",
			"lib/session/roller.js",
			"lib/session/session.js",
			"classes/ortak/cEnc.js",
			"classes/mq/mqYapi.js",
			"classes/mq/mqAlt.js",
			"classes/mq/tekil/mqParam.js",
			"classes/mq/cogul/mqCogul.js"
		};

		public JSCoreLib() : base() =>
			add(StaticLibs);
	}

	public class JSBootCode : Cache {
		public const string
			CF_Pre = @"jint/lib/preboot",
			CF_Post = @"jint/lib/postboot";

		public JsLib Pre { get; set; } = JsLib.From(CF_Pre);
		public JsLib Post { get; set; } = JsLib.From(CF_Post);

		public override void Dispose() {
			base.Dispose();
			Pre?.Dispose(); Post?.Dispose();
			Pre = Post = null;
		}
		public override bool fill(string rootDir = null) =>
			base.fill(rootDir) && Pre.fill(rootDir) && Post.fill(rootDir);
	}
}
