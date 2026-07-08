using System;

namespace CJintLib {
	public class Cache : CObject, IDisposable {
		public DateTime? CacheTS { get; set; }
		public virtual bool NeedsUpdate {
			get { return CacheTS.bosMu(); }
		}

		public virtual void Dispose() =>
			CacheTS = null;
		public static bool Fill<T>(out T t) where T : Cache, new() {
			var res = new T();
			var ok = res.fill();
			t = ok ? res : null;
			return ok;
		}
		public bool sync() =>
			NeedsUpdate ? fill() : true;
		public virtual bool fill() =>
			true;
	}
}
