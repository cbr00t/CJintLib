using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Acornima.Ast;
using Jint;
using Jint.Native;

using Newtonsoft.Json.Linq;

namespace CJintLib {
	public class JsLib : Cache, IEnumerable<Prepared<Script>> {
		public const string DelimList = "|";
		public const string CF_Config = "config.php";

		static string _origin, _webSrvRoot, _wwwRoot, _jintRoot;
		static CDict<string, Prepared<Script>> key2ScriptCache;
		protected Prepared<Script>? script;

		public static string WebSrvRoot {
			get => _webSrvRoot.emptyCoalesceB(() => "/appserv/www");
			set => _webSrvRoot = value;
		}
		public static string WWWRoot {
			get => _wwwRoot.emptyCoalesceB(() => WebSrvRoot + "/skyerp");
			set => _wwwRoot = value;
		}
		public static string JintRoot {
			get => _jintRoot.emptyCoalesceB(() => WWWRoot + "/jint");
			set => _jintRoot = value;
		}
		public static string Origin {
			get => _origin.emptyCoalesceB(() =>
				$"https://{CGlobals.VioCloudServer_HostName}:90");
			set => _origin = value;
		}
		public static string ConfigFile {
			get => Path.Combine(WWWRoot, CF_Config);
		}
		public IEnumerable<string> LibFiles {
			get => Libs.Select(n =>
				Path.Combine(WWWRoot, $"{n}.js"));
		}
		public static CDict<string, Prepared<Script>> Key2ScriptCache {
			get {
				if (key2ScriptCache == null)
					key2ScriptCache = new CDict<string, Prepared<Script>>();
				return key2ScriptCache;
			}
		}
		public Prepared<Script>? Script {
			get => script;
			set => script = value;
		}
		public static bool NeedsUpdate {
			get {
				if (CacheTS.bosMu())
					return true;

				if (DateTime.Now.Subtract(CacheTS.Value) < TimeSpan.FromSeconds(10))
					return false;

				var ts = head(CF_Config, WWWRoot);
				return ts.bosMu() || ts > CacheTS;
			}
		}
		public CList<string> Libs { get; set; } = new CList<string>();

		public JsLib() : base() { }
		public override void Dispose() {
			base.Dispose();
			script = null;
			Libs?.Clear();
			Libs = null;
		}

		public static T From<T>(params string[] names) where T : JsLib, new() =>
			From<T>(names as IEnumerable<string>);
		public static JsLib From(IEnumerable<string> names) =>
			From<JsLib>(names);
		public static JsLib From(params string[] names) =>
			From<JsLib>(names);
		public static T From<T>(IEnumerable<string> names) where T : JsLib, new() {
			var res = new T();
			if (names.bosMu())
				return default;
			res.add(names);
			return res;
		}

		public override bool fill(string rootDir = null) =>
			base.fill(rootDir) && fetchLibs(out script, Libs, rootDir);
		public static bool fetchLibs(out Prepared<Script>? t, params string[] names) =>
			fetchLibs(out t, names as IEnumerable<string>);
		public static bool fetchLibs(out Prepared<Script>? t, IEnumerable<string> names, string rootDir = null) {
			t = null;
			if (names.bosMu())
				return true;

			var files = new CList<string>();
			foreach (var _n in names.Where(_ => _.bosDegilMi()))
				files.Add(normalized(_n, ""));

			if (files.bosMu())
				return true;

			invalidateCache();
			var cacheKey = string.Join(DelimList, files);
			var cache = Key2ScriptCache;
			if (cache != null) {
				lock (cache) {
					if (cache.TryGetValue(cacheKey, out var cached)) {
						t = cached;
						return true;
					}
				}
			}

			var f2d = new CDict<string, string>();
			Parallel.ForEach(
				files,
				new ParallelOptions {
					MaxDegreeOfParallelism = 4,
					TaskScheduler = TaskScheduler.Default
				},
				f => {
					var d = fetch(f, rootDir);
					if (d.bosDegilMi()) {
						lock (f2d)
							f2d[f] = d;
					}
				}
			);

			var sb = new StringBuilder();
			foreach (var f in files) {
				var d = f2d.atIfAbsent(f);
				if (d.bosMu())
					continue;

				sb.AppendLine($"\n//# sourceURL=[{f.relativePathFor(rootDir)}\n");
				d = JsEngine.fixEvalCode(d);
				sb.AppendLine(d);
				sb.AppendLine();
			}
			f2d.Clear();
			files.Clear();

			var code = sb.ToString();
			sb.Clear();

			if (code.bosMu())
				return true;

			var res = Engine.PrepareScript(code, code, true);
			t = res;
			if (!res.IsValid)
				return false;

			CacheTS = DateTime.Now;
			if (cache != null) {
				lock (cache)
					cache[cacheKey] = res;
			}

			return true;
		}

		public JsValue eval(JsEngine je, bool sync = false, int? timeout = null) =>
			je?.eval(this, sync, timeout);
		public JsValue evalAsync(JsEngine je, int? timeout = null) =>
			je?.eval(this, false, timeout);
		public JsValue evalSync(JsEngine je, int? timeout = null) =>
			je?.eval(this, true, timeout);

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

		public static bool invalidateCache() {
			if (!NeedsUpdate || key2ScriptCache == null)
				return false;

			lock (key2ScriptCache)
				key2ScriptCache.Clear();
			return true;
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

		public static DateTime? head(string n, string rootDir = null) {
			var req = getWebReq(n, rootDir, true);
			if (req == null)
				return null;

			using (var resp = (HttpWebResponse)req.GetResponse())
				return resp.LastModified;
		}
		public static string fetch(string n, string rootDir = null) {
			string withCache(bool cached) {
				var req = getWebReq(n, rootDir, false, cached);
				if (req == null)
					return null;

				using (var resp = (HttpWebResponse)req.GetResponse())
				using (var srm = resp.GetResponseStream())
				using (var sr = srm.wsGetStreamReader())
					return sr.ReadToEnd();
			}
			try { return withCache(false); }
			catch (WebException) { return withCache(true); }
		}
		public static HttpWebRequest getWebReq(string n, string rootDir = null, bool head = false, bool cached = false) {
			rootDir = rootDir ?? WWWRoot;
			rootDir = rootDir.relativePathFor(WebSrvRoot).Replace('\\', '/').Trim('/');
			
			var f = normalized(n, rootDir)
				.Replace(WebSrvRoot, "")
				.Trim('/');

			var origin = Origin;
			var isLocal = new Uri(origin).IsLoopback;
			var url = $"{origin}/{f}";

			var req = WebRequest.CreateHttp(url);
			req.Method = head ? "HEAD" : "GET";
			req.CachePolicy = new RequestCachePolicy(cached ? RequestCacheLevel.CacheIfAvailable : RequestCacheLevel.Revalidate);
			req.Timeout = req.ReadWriteTimeout = isLocal ? 100 : 1_000;
			req.AllowAutoRedirect = true;
			req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

			return req;
		}

		public static string normalized(string n, string rootDir = null) {
			rootDir = rootDir ?? WWWRoot;
			if (rootDir.bosDegilMi() && !Path.IsPathRooted(n))
				n = rootDir.pathCombine(n);
			if ((Path.GetExtension(n)?.Length ?? 0) < 2)
				n += ".js";
			n = n.Replace('\\', '/');
			return n;
		}
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
