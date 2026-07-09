using System;

namespace CJintLib {
	public class Cache : CObject, IDisposable {
		public DateTime? CacheTS { get; set; }
		public virtual bool NeedsUpdate {
			get { return CacheTS.bosMu(); }
		}

		public virtual void Dispose() =>
			CacheTS = null;
		public static bool Fill<T>(out T t, string rootDir = null) where T : Cache, new() {
			var res = new T();
			var ok = res.fill(rootDir);
			t = ok ? res : null;
			return ok;
		}
		public bool sync(string rootDir = null) =>
			NeedsUpdate ? fill(rootDir) : true;
		public virtual bool fill(string rootDir = null) =>
			true;
	}
}
