using System;

namespace CJintLib {
	public class Cache : CObject, IDisposable {
		public static DateTime? CacheTS { get; set; }

		public virtual void Dispose() =>
			CacheTS = null;
		public static bool Fill<T>(out T t, string rootDir = null) where T : Cache, new() {
			var res = new T();
			var ok = res.fill(rootDir);
			t = ok ? res : null;
			return ok;
		}
		public virtual bool fill(string rootDir = null) =>
			true;
	}
}
