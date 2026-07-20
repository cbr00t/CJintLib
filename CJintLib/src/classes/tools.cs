using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Jint.Native;
using Jint.Native.Object;

using Newtonsoft.Json;
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

			try {
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
			catch (IOException) { }
		}
	}

	public class JsToolAttribute : Attribute { }

	public class JsTools : JsBase {
		public delegate JsLib ImportLibsProc(params string[] names);

		[JsTool]
		public JToken js(object v) =>
			v.isNull() ? null :
			v is ObjectInstance oi ? JToken.FromObject(oi.ToObject()) :
			v is JToken tkn ? tkn :
			JToken.FromObject(v);

		/*[JsTool]
		public JToken asJS(object v) =>
			v.isNull() ? null :
			v is ObjectInstance oi ? JToken.FromObject(oi.ToObject()) :
			v is JToken tkn ? tkn :
			v is string str ? JToken.Parse(str) : null;*/

		[JsTool]
		public JsLib cimport(params string[] names) {
			var eng = Engine;
			var lib = JsLib.From(names);
			if (lib == null || !lib.fill(JsLib.JintRoot))
				throw new FileLoadException($"failed to load libs: {string.Join(" | ", names)}");
			lib?.eval(eng);
			// eng.Con.debug($"imported: {string.Join(" | ", names)}");
			return lib;
		}

		[JsTool]
		public async Task<JsFetchResponse> fetch(string url, JObject args = null) {
			if (string.IsNullOrWhiteSpace(url))
				throw new ArgumentException("fetch url boş olamaz", nameof(url));

			args = args ?? new JObject();

			var method = (args.s("method") ?? "GET")
				.Trim()
				.ToUpperInvariant();

			var timeout = (args.ni("timeout") ?? 0)
				.emptyCoalesce(30_000);

			var rwTimeout = args.ni("rwTimeout") ?? timeout;
			var headers = args.o("headers");
			var body = args["body"];

			var req = HttpWebRequest.CreateHttp(url);

			req.Method = method;
			req.Timeout = timeout;
			req.ReadWriteTimeout = rwTimeout;
			req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.Revalidate);
			req.Proxy = WebProxy.GetDefaultProxy() ?? WebRequest.DefaultWebProxy;
			req.AllowAutoRedirect = args.Value<bool?>("redirect") ?? true;

			req.AutomaticDecompression =
				DecompressionMethods.GZip |
				DecompressionMethods.Deflate;

			req.ServicePoint.Expect100Continue = false;

			JsFetchHelper.ApplyRequestHeaders(req, headers);

			var hasBody =
				body != null &&
				body.Type != JTokenType.Null &&
				method != "GET" &&
				method != "HEAD";

			if (hasBody) {
				string inferredContentType;

				var bodyBytes = JsFetchHelper.GetRequestBodyBytes(
					body,
					req.ContentType,
					out inferredContentType
				);

				if (string.IsNullOrWhiteSpace(req.ContentType))
					req.ContentType = inferredContentType;

				/*
				 * Content-Length kullanıcı header'ından değil, gerçek byte
				 * uzunluğundan belirleniyor. Böylece UTF-8 karakterlerinde
				 * karakter sayısı / byte sayısı farkı sorun oluşturmaz.
				 */
				req.ContentLength = bodyBytes.LongLength;
				req.SendChunked = false;

				using (var stream = req.GetRequestStream())
					stream.Write(bodyBytes, 0, bodyBytes.Length);
			}
			else if (method != "GET" && method != "HEAD") {
				req.ContentLength = 0;
				req.SendChunked = false;
			}

			HttpWebResponse resp;

			try {
				resp = (HttpWebResponse)req.GetResponse();
			}
			catch (WebException ex) {
				/*
				 * HttpWebRequest, HTTP 4xx ve 5xx cevaplarını exception olarak
				 * verir. Browser fetch() ise bunları normal Response olarak döndürür.
				 */
				resp = ex.Response as HttpWebResponse;

				if (resp == null)
					throw;
			}

			using (resp) {
				var responseUrl = resp.ResponseUri?.AbsoluteUri ?? url;
				return new JsFetchResponse {
					url = responseUrl,
					status = (int)resp.StatusCode,
					statusText = resp.StatusDescription,
					redirected = !string.Equals(
						responseUrl,
						url,
						StringComparison.OrdinalIgnoreCase
					),
					headers = JsFetchHelper.ReadResponseHeaders(resp),
					encoding = JsFetchHelper.GetResponseEncoding(resp),
					bodyBytes = JsFetchHelper.ReadResponseBytes(resp)
				};
			}
		}

		#region Classes
		public sealed class JsFetchResponse {
			internal byte[] bodyBytes;
			internal Encoding encoding;

			public string url { get; internal set; }
			public int status { get; internal set; }
			public string statusText { get; internal set; }
			public bool ok {
				get { return status >= 200 && status < 300; }
			}
			public bool redirected { get; internal set; }
			public JObject headers { get; internal set; }

			public string text() {
				var bytes = bodyBytes ?? new byte[0];
				var enc = encoding ?? Encoding.UTF8;

				var res = enc.GetString(bytes);
				if (res.bosDegilMi() && (int)res[0] == 65279)
					res = res.Substring(1);
				if (res.hasUTF8Signature())
					res = res.Substring(3);

				return res;
			}

			public object json() {
				var value = text();
				if (string.IsNullOrWhiteSpace(value))
					return null;

				return JToken.Parse(value);
			}

			public byte[] bytes() {
				return bodyBytes ?? new byte[0];
			}

			public override string ToString() {
				return string.Format(
					"Response {{ status: {0}, ok: {1}, url: \"{2}\" }}",
					status,
					ok,
					url
				);
			}
		}

		public static class JsFetchHelper {
			public static void ApplyRequestHeaders(
				HttpWebRequest req,
				JObject headers) {
				if (req == null)
					throw new ArgumentNullException(nameof(req));

				if (headers == null)
					return;

				foreach (var item in headers.Properties()) {
					var name = item.Name;
					var value = HeaderValueToString(item.Value);

					if (string.IsNullOrEmpty(name) || value == null)
						continue;

					switch (name.Trim().ToLowerInvariant()) {
						case "content-type":
							req.ContentType = value;
							break;

						/*
						 * Content-Length body byte dizisi oluşturulduktan sonra
						 * fetch() içinde hesaplanıyor.
						 */
						case "content-length":
							break;

						case "accept":
							req.Accept = value;
							break;

						case "user-agent":
							req.UserAgent = value;
							break;

						case "referer":
							req.Referer = value;
							break;

						case "host":
							req.Host = value;
							break;

						case "connection":
							req.Connection = value;
							break;

						case "expect":
							req.Expect = value;
							break;

						case "if-modified-since": {
							DateTime modified;

							if (DateTime.TryParse(value, out modified))
								req.IfModifiedSince = modified;

							break;
						}

						case "range":
							ApplyRangeHeader(req, value);
							break;

						case "transfer-encoding":
							req.TransferEncoding = value;
							req.SendChunked = true;
							break;

						case "accept-encoding":
							/*
							 * AutomaticDecompression kullanıldığı için bunu
							 * HttpWebRequest'in yönetmesi daha güvenlidir.
							 */
							break;

						default:
							req.Headers[name] = value;
							break;
					}
				}
			}


			private static string HeaderValueToString(JToken value) {
				if (value == null || value.Type == JTokenType.Null)
					return null;

				var array = value as JArray;

				if (array != null) {
					var values = new string[array.Count];

					for (var i = 0; i < array.Count; i++)
						values[i] = array[i]?.ToString() ?? "";

					return string.Join(", ", values);
				}

				return value.ToString();
			}


			private static void ApplyRangeHeader(
				HttpWebRequest req,
				string value) {
				if (string.IsNullOrWhiteSpace(value))
					return;

				// Örnekler:
				// bytes=100-500
				// bytes=100-

				var parts = value.Split(new[] { '=' }, 2);

				if (parts.Length != 2)
					return;

				var unit = parts[0].Trim();
				var rangeValue = parts[1].Trim();

				/*
				 * Şimdilik tek bir range destekleniyor.
				 * bytes=0-100,200-300 gibi çoklu range uygulanmıyor.
				 */
				if (rangeValue.IndexOf(',') >= 0)
					return;

				var bounds = rangeValue.Split(new[] { '-' }, 2);

				if (bounds.Length != 2)
					return;

				long from;

				if (!long.TryParse(bounds[0], out from))
					return;

				long to;

				if (long.TryParse(bounds[1], out to))
					req.AddRange(unit, from, to);
				else
					req.AddRange(unit, from);
			}


			public static byte[] GetRequestBodyBytes(
				JToken body,
				string contentType,
				out string inferredContentType) {
				inferredContentType = null;

				if (body == null || body.Type == JTokenType.Null)
					return new byte[0];

				if (body.Type == JTokenType.Bytes) {
					inferredContentType = "application/octet-stream";
					return body.Value<byte[]>() ?? new byte[0];
				}

				string text;

				switch (body.Type) {
					case JTokenType.String:
						text = body.Value<string>() ?? "";
						inferredContentType = "text/plain; charset=utf-8";
						break;

					default:
						text = body.ToString(Formatting.None);
						inferredContentType = "application/json; charset=utf-8";
						break;
				}

				var bodyEncoding =
					GetEncodingFromContentType(contentType) ??
					Encoding.UTF8;

				return bodyEncoding.GetBytes(text);
			}


			public static byte[] ReadResponseBytes(
				HttpWebResponse resp) {
				if (resp == null)
					return new byte[0];

				using (var stream = resp.GetResponseStream()) {
					if (stream == null)
						return new byte[0];

					using (var output = new MemoryStream()) {
						stream.CopyTo(output);
						return output.ToArray();
					}
				}
			}


			public static JObject ReadResponseHeaders(
				HttpWebResponse resp) {
				var result = new JObject();

				if (resp == null || resp.Headers == null)
					return result;

				foreach (var key in resp.Headers.AllKeys) {
					if (key == null)
						continue;

					var values = resp.Headers.GetValues(key);

					if (values == null || values.Length == 0)
						result[key] = JValue.CreateNull();
					else if (values.Length == 1)
						result[key] = values[0];
					else
						result[key] = new JArray(values);
				}

				return result;
			}


			public static Encoding GetResponseEncoding(
				HttpWebResponse resp) {
				if (resp == null)
					return Encoding.UTF8;

				if (!string.IsNullOrWhiteSpace(resp.CharacterSet)) {
					try {
						return Encoding.GetEncoding(resp.CharacterSet);
					}
					catch {
						// Geçersiz veya sistemde bulunmayan charset.
					}
				}

				return
					GetEncodingFromContentType(resp.ContentType) ??
					Encoding.UTF8;
			}


			public static Encoding GetEncodingFromContentType(
				string contentType) {
				if (string.IsNullOrWhiteSpace(contentType))
					return null;

				var parts = contentType.Split(';');

				foreach (var part in parts) {
					var value = part.Trim();

					if (!value.StartsWith(
						"charset=",
						StringComparison.OrdinalIgnoreCase)) {
						continue;
					}

					var charset = value
						.Substring("charset=".Length)
						.Trim()
						.Trim('"', '\'');

					if (string.IsNullOrWhiteSpace(charset))
						return null;

					try {
						return Encoding.GetEncoding(charset);
					}
					catch {
						return null;
					}
				}

				return null;
			}
		}
		#endregion
	}
}
